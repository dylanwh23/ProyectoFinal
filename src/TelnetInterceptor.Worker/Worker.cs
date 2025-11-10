using System.Net.Sockets;
using System.Text;
using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shared.Contracts;
using TelnetInterceptor.Worker.Configuration;
using TelnetInterceptor.Worker.Services;
using TelnetInterceptor.Worker.Models;
using System.Diagnostics;

namespace TelnetInterceptor.Worker;

// Define the event contract locally
public record CameraDeletedEvent(string IpCamara);

// Define the consumer for the CameraDeletedEvent
public class CameraDeletedConsumer : IConsumer<CameraDeletedEvent>
{
    private readonly Worker _worker; // Dependency on the Worker class

    // Note: Injecting Worker directly into a consumer can lead to lifetime issues or
    // circular dependencies if not managed carefully. A better approach might be to
    // inject a service that the Worker exposes or uses for connection management.
    // However, for this immediate fix, we'll inject Worker directly.
    public CameraDeletedConsumer(Worker worker)
    {
        _worker = worker;
    }

public Task Consume(ConsumeContext<CameraDeletedEvent> context)
    {
        // Llama al nuevo método para detener la conexión de la cámara
        return _worker.DetenerConexionCamara(context.Message.IpCamara);
    }
}

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ConfiguracionInterceptor _configuracion;
    private readonly IServicioFiltradoEventos _servicioFiltrado;
    private readonly IGestorEndpointsCamaras _gestorCamaras;

    private readonly Dictionary<string, TcpClient> _clients = new();
    private readonly Dictionary<string, EstadisticasCamara> _estadisticas = new();
    private readonly Dictionary<string, CancellationTokenSource> _cancellationSources = new();

    private readonly int _port;
    private readonly string _ipEscucha;

    private readonly HashSet<string> _camarasConectando = new();

    public IEnumerable<EstadisticasCamara> ObtenerEstadisticas() => _estadisticas.Values.ToList();

    public Worker(
        ILogger<Worker> logger,
        IOptions<ConfiguracionInterceptor> configuracion,
        IServicioFiltradoEventos servicioFiltrado,
        IBus bus, // IBus is needed for publishing events, but not directly for consuming them here.
        IGestorEndpointsCamaras gestorCamaras)
    {
        _logger = logger;
        _configuracion = configuracion.Value;
        _servicioFiltrado = servicioFiltrado;
        _gestorCamaras = gestorCamaras;

        _port = _configuracion.PuertoTcp;
        _ipEscucha = _configuracion.IpEscucha;
    }

    public Task DetenerConexionCamara(string ipCamara)
    {
        _logger.LogInformation("🛑 Deteniendo intentos de conexión para la cámara {ip}", ipCamara);

        if (_cancellationSources.TryGetValue(ipCamara, out var cts))
        {
            cts.Cancel();
            _cancellationSources.Remove(ipCamara);
        }

        if (_clients.TryGetValue(ipCamara, out var client))
        {
            client.Close();
            _clients.Remove(ipCamara);
        }

        _estadisticas.Remove(ipCamara);
        _camarasConectando.Remove(ipCamara);

        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("⏳ Iniciando ciclo de conexión de cámaras...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var camaras = _gestorCamaras.ObtenerCamaras().ToList();

                foreach (var ip in camaras)
                {
                    if (!_clients.ContainsKey(ip))
                    {
                        _ = Task.Run(() => IniciarConexionCamara(ip, stoppingToken), stoppingToken);
                    }
                }

                await Task.Delay(3000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error en el ciclo principal: {error}", ex.Message);
                await Task.Delay(30000, stoppingToken);
            }
        }

        foreach (var client in _clients.Values)
            client.Close();

        _clients.Clear();
        _cancellationSources.Clear();
    }

    private async Task IniciarConexionCamara(string ipCamara, CancellationToken globalToken)
{
    if (!_camarasConectando.Add(ipCamara)) return;
    var cts = CancellationTokenSource.CreateLinkedTokenSource(globalToken);
    var token = cts.Token;
    _cancellationSources[ipCamara] = cts;

    try
    {
        while (!token.IsCancellationRequested)
        {
            _logger.LogInformation("Intentando conectar a {ip} a las {hora}", ipCamara, DateTime.UtcNow);

            var client = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_configuracion.TimeoutConexionSegundos));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            try
            {
                await client.ConnectAsync(ipCamara, _port, linkedCts.Token);

                _clients[ipCamara] = client;
                _estadisticas[ipCamara] = new EstadisticasCamara(ipCamara, _port)
                {
                    EstaConectada = true,
                    UltimoMensaje = "Conectada",
                    HoraUltimoMensaje = DateTime.UtcNow
                };

                _logger.LogInformation("✅ Conectado a cámara {ip}", ipCamara);

                await LeerMensajesCamara(ipCamara, client, token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("⚠️ Tiempo de conexión a cámara {ip} excedido", ipCamara);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("⚠️ Cámara {ip} desconectada o inaccesible: {error}", ipCamara, ex.Message);
            }
            finally
            {
                client.Close();
                _clients.Remove(ipCamara, out _);
                if (_estadisticas.ContainsKey(ipCamara))
                    _estadisticas[ipCamara].EstaConectada = false;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_configuracion.IntervaloReintentoSegundos), token); }
            catch (OperationCanceledException) { break; }
        }
    }
    finally
    {
        _camarasConectando.Remove(ipCamara);
    }
}


    private async Task LeerMensajesCamara(string ipCamara, TcpClient client, CancellationToken token)
    {
        _estadisticas.TryGetValue(ipCamara, out var stats);
        using var stream = client.GetStream();
        var buffer = new byte[1024];

        while (!token.IsCancellationRequested && client.Connected)
        {
            try
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                if (bytesRead == 0) break;

                string mensaje = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                if (string.IsNullOrWhiteSpace(mensaje)) continue;

                stats!.MensajesRecibidos++;
                stats.UltimoMensaje = mensaje;
                stats.HoraUltimoMensaje = DateTime.UtcNow;

                _logger.LogInformation("[{ip}] {msg}", ipCamara, mensaje);

                if (_servicioFiltrado.DebeProcesarEvento(ipCamara, mensaje))
                {
                    var evento = new EventoMovimientoDetectado
                    {
                        IpCamara = ipCamara,
                        Momento = DateTime.UtcNow,
                        MensajeCrudoEvento = mensaje
                    };

                    await _gestorCamaras.PublicarEvento(evento!, token);
                }
            }
            catch (IOException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error en lectura de {ip}: {err}", ipCamara, ex.Message);
                break;
            }
        }

        client.Close();
        _clients.Remove(ipCamara, out _);

        if (stats != null)
        {
            stats.EstaConectada = false;
            stats.UltimoMensaje = "Desconectada";
            stats.HoraUltimoMensaje = DateTime.UtcNow;
        }
    }
}
