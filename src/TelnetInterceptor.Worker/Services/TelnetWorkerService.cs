using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Contracts;
using Shared.Contracts.Models; // Para AltaEventoModel
using TelnetInterceptor.Worker.Configuration;
// using TelnetInterceptor.Worker.Controllers; // No es necesario EventosController directamente
using TelnetInterceptor.Worker.Models;

namespace TelnetInterceptor.Worker.Services;

public class TelnetWorkerService : BackgroundService
{
    private readonly ILogger<TelnetWorkerService> _logger;
    private readonly CameraManagerService _cameraManager;
    private readonly IBus _bus;
    private readonly ConfiguracionInterceptor _config;
    private readonly IServiceScopeFactory _scopeFactory; // Inyectar IServiceScopeFactory para crear un scope para el EventStorageService

    // Estado de Conexiones
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private readonly ConcurrentDictionary<string, EstadisticasCamara> _stats = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationSources = new();

    // Estado para "Cooldown" (Evitar spam de eventos)
    private readonly ConcurrentDictionary<string, string> _ultimoMsg = new();
    private readonly ConcurrentDictionary<string, DateTime> _ultimoTime = new();

    // Control de concurrencia
    private readonly ConcurrentDictionary<string, bool> _conectando = new();

    public TelnetWorkerService(
        ILogger<TelnetWorkerService> logger,
        CameraManagerService cameraManager,
        IBus bus,
        IOptions<ConfiguracionInterceptor> config,
        IServiceScopeFactory scopeFactory) // Reemplazar EventosController por IServiceScopeFactory
    {
        _logger = logger;
        _cameraManager = cameraManager;
        _bus = bus;
        _config = config.Value;
        _scopeFactory = scopeFactory;
    }

    // Método usado por el Endpoint Telnet/Status
    public Dictionary<string, EstadisticasCamara> ObtenerEstadisticas() 
        => _stats.ToDictionary(k => k.Key, v => v.Value);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔵 TelnetWorkerService Iniciado (Conexiones + Eventos)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Preguntamos al Manager qué cámaras existen en BD
                var camaras = await _cameraManager.ObtenerCamarasBd();
                var ipsActivas = camaras.Select(c => c.IpCamara).ToHashSet();

                // 2. Desconectamos las que fueron borradas de la BD
                foreach (var ip in _clients.Keys)
                {
                    if (!ipsActivas.Contains(ip)) DesconectarCamara(ip);
                }

                // 3. Conectamos las nuevas o desconectadas
                foreach (var cam in camaras)
                {
                    // Si no está conectada Y no se está intentando conectar ahora mismo
                    if (!_clients.ContainsKey(cam.IpCamara) && !_conectando.ContainsKey(cam.IpCamara))
                    {
                        _ = ConectarYEscuchar(cam, stoppingToken);
                    }
                }

                await Task.Delay(5000, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error en ciclo Telnet: {msg}", ex.Message);
                await Task.Delay(10000, stoppingToken);
            }
        }
    }

    private async Task ConectarYEscuchar(EstadisticasCamara cam, CancellationToken appToken)
    {
        var ip = cam.IpCamara;
        if (!_conectando.TryAdd(ip, true)) return; // Lock simple

        var cts = CancellationTokenSource.CreateLinkedTokenSource(appToken);
        _cancellationSources[ip] = cts;

        try
        {
            _logger.LogInformation("⏳ Conectando a {Ip}:{Port}...", ip, cam.Puerto);
            var client = new TcpClient();
            
            // Timeout de conexión 5s
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
            
            await client.ConnectAsync(ip, cam.Puerto, linked.Token);

            _clients[ip] = client;
            
            // Inicializamos stats
            _stats[ip] = new EstadisticasCamara(ip, cam.Puerto, cam.RutaCarpeta, cam.Nombre) 
            { 
                EstaConectada = true, 
                UltimoMensaje = "Conectada",
                HoraUltimoMensaje = DateTime.UtcNow 
            };
            
            _logger.LogInformation("🔌 Conectado a {Ip}", ip);

            using var stream = client.GetStream();
            var buffer = new byte[1024];

            while (client.Connected && !cts.Token.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, cts.Token);
                if (bytesRead == 0) break; 

                string msg = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    await ProcesarMensaje(ip, msg);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Desconexión en {Ip}: {Msg}", ip, ex.Message);
        }
        finally
        {
            DesconectarCamara(ip);
            _conectando.TryRemove(ip, out _);
        }
    }

    private async Task ProcesarMensaje(string ip, string msg)
    {
        // Actualizar estadísticas visuales
        if (_stats.TryGetValue(ip, out var s))
        {
            s.MensajesRecibidos++;
            s.UltimoMensaje = msg;
            s.HoraUltimoMensaje = DateTime.UtcNow;
        }

        // --- LÓGICA DE FILTRADO (COOLDOWN) ---
        var ahora = DateTime.UtcNow;
        var cooldown = TimeSpan.FromSeconds(2); // Hardcoded o desde _config

        // Si es el mismo mensaje que el anterior y pasó poco tiempo, ignorar
        if (_ultimoMsg.TryGetValue(ip, out var lastMsg) && lastMsg == msg)
        {
            if (_ultimoTime.TryGetValue(ip, out var lastTime) && (ahora - lastTime) < cooldown)
            {
                return; // SPAM DETECTADO
            }
        }

        _ultimoMsg[ip] = msg;
        _ultimoTime[ip] = ahora;

        _logger.LogInformation("📨 [{Ip}] Evento: {Msg}", ip, msg);

        // --- GUARDAR EVENTO EN LA BASE DE DATOS A TRAVÉS DE IEventStorageService ---
        using var scope = _scopeFactory.CreateScope();
        var eventStorageService = scope.ServiceProvider.GetRequiredService<IEventStorageService>();

        try
        {
            var puertoCamara = _stats.TryGetValue(ip, out var statsCamara) ? statsCamara.Puerto : 23; // Obtener el puerto de las estadísticas

            var eventoParaGuardar = new AltaEventoModel
            {
                Nombre = "Evento Telnet: " + msg,
                IpCamara = ip,
                Puerto = puertoCamara, 
                EsEventoGuardado = false, // Es un evento en tiempo real
                FechaEvento = DateTime.UtcNow,
                Descripcion = msg
            };

            EventFrameRange? frameRange = null; // Declarar frameRange aquí
            var latestFramePath = _cameraManager.GetLatestFile(ip);
            if (!string.IsNullOrEmpty(latestFramePath))
            {
                frameRange = _cameraManager.GetFrameRangeForEvent(latestFramePath, _config.FramesAdyacentesTelnet); // Usar la configuración
                if (frameRange != null)
                {
                    eventoParaGuardar.FramePath = latestFramePath; // El frame "central"
                    eventoParaGuardar.FromFrame = frameRange.FromFrame;
                    eventoParaGuardar.ToFrame = frameRange.ToFrame;
                    eventoParaGuardar.RutaCarpeta = frameRange.FolderPath;
                }
                else
                {
                    _logger.LogWarning("No se pudo determinar el rango de frames para el evento Telnet en {Ip} con frame {FramePath}", ip, latestFramePath);
                    eventoParaGuardar.FramePath = latestFramePath; // Fallback al frame individual
                }
            }
            else
            {
                _logger.LogWarning("No se encontró el último frame para la cámara {Ip}. El evento Telnet se guardará sin FramePath.", ip);
            }

            // Asignar RutaCarpeta: primero desde frameRange si está disponible, sino desde CameraManagerService, sino cadena vacía.
            // Nota: camaraRutaCarpeta puede ser nulo si la cámara fue eliminada de la BD.
            eventoParaGuardar.RutaCarpeta = frameRange?.FolderPath ?? _cameraManager.ObtenerRutaCarpeta(ip) ?? string.Empty;

            await eventStorageService.SaveEventAsync(eventoParaGuardar);

            // También podemos mantener la publicación a RabbitMQ si es necesaria para otros servicios
            var queueName = ip.Replace('.', '_');
            var uri = new Uri($"queue:{queueName}");
            var endpoint = await _bus.GetSendEndpoint(uri);
            
            await endpoint.Send(new Shared.Contracts.Models.EventoMovimientoDetectado // Especificar el namespace
            { 
                IpCamara = ip, 
                MensajeCrudoEvento = msg, 
                Momento = DateTime.UtcNow 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error procesando evento Telnet para {Ip}: {Msg}", ip, ex.Message);
        }
    }

    public void DesconectarCamara(string ip)
    {
        if (_clients.TryRemove(ip, out var client)) 
            try { client.Close(); } catch { }
        
        if (_cancellationSources.TryRemove(ip, out var cts)) 
            cts.Cancel();

        if (_stats.TryGetValue(ip, out var s))
        {
            s.EstaConectada = false;
            s.UltimoMensaje = "Desconectada";
        }
    }
}
