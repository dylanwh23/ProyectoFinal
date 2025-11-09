using System.Net.Sockets;
using System.IO;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shared.Contracts; // Necesitas la clase EventoMovimientoDetectado aquí
using TelnetInterceptor.Worker.Configuration;
using TelnetInterceptor.Worker.Services;
using MassTransit;

namespace TelnetInterceptor.Worker;

// Worker (Cliente TCP) que se conecta al servidor de eventos (AutoVision/Cámara).
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ConfiguracionInterceptor _configuracion;
    private readonly IServicioFiltradoEventos _servicioFiltrado;
    private readonly IPublishEndpoint _puntoPublicacion;
    
    private TcpClient? _client;
    private readonly string _serverIp;
    private readonly int _port;

    public Worker(
        ILogger<Worker> logger,
        IOptions<ConfiguracionInterceptor> configuracion,
        IServicioFiltradoEventos servicioFiltrado,
        IPublishEndpoint puntoPublicacion)
    {
        _logger = logger;
        _configuracion = configuracion.Value;
        _servicioFiltrado = servicioFiltrado;
        _puntoPublicacion = puntoPublicacion;

        // Configurable: Obtiene IP y Puerto de appsettings.json
        _serverIp = _configuracion.IpEscucha; // La IP donde el servidor (cámara) está corriendo
        _port = _configuracion.PuertoTcp;     // El puerto que el servidor (cámara) usa para emitir
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting to Event Server at {ip}:{port}...", _serverIp, _port);
                
                _client = new TcpClient();
                // CRÍTICO: Usamos Task.Delay para implementar un timeout de conexión
                await _client.ConnectAsync(_serverIp, _port).WaitAsync(TimeSpan.FromSeconds(5), stoppingToken);
                
                _logger.LogInformation("Connected successfully to Event Server.");
                
                // Pasa la responsabilidad de lectura, filtrado y publicación
                await ProcessAutoVisionEvents(_client, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // El servicio fue detenido
                _logger.LogInformation("Worker client stopping...");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Connection error: {error}. Retrying in 5 seconds...", ex.Message);
                await Task.Delay(5000, stoppingToken); // Esperar 5 segundos antes de reintentar
            }
            finally
            {
                _client?.Close();
                _client = null;
            }
        }
    }

    // Método principal para leer, filtrar y publicar eventos
    private async Task ProcessAutoVisionEvents(TcpClient client, CancellationToken stoppingToken)
    {
        // La IP de la cámara ahora es la IP del servidor al que nos conectamos
        string ipCamara = _serverIp; 
        
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            // 1. Recibir flujo constante de eventos
            while (client.Connected && !stoppingToken.IsCancellationRequested)
            {
                // El uso de DataAvailable seguido de Task.Delay es el patrón que te funcionó
                if (stream.DataAvailable)
                {
                    string? mensajeEvento = await reader.ReadLineAsync(stoppingToken);
                    if (mensajeEvento == null) break;
                    
                    _logger.LogInformation("Raw Event: {data}", mensajeEvento);

                    // 2. Filtrar el evento antes de mandarlo (lógica stateful)
                    if (_servicioFiltrado.DebeProcesarEvento(ipCamara))
                    {
                        // 3. Asignar TimeStamp y Categorizar (Crear el Contrato)
                        var eventoMovimiento = new EventoMovimientoDetectado 
                        {
                            IpCamara = ipCamara, // Categorización: la IP del servidor que nos lo envió
                            Momento = DateTime.UtcNow, 
                            MensajeCrudoEvento = mensajeEvento
                        };
                        
                        // 4. Publicar en EventBus (RabbitMQ)
                        await _puntoPublicacion.Publish(eventoMovimiento, stoppingToken);
                        _logger.LogInformation("Válido: Evento de {ip} publicado en EventBus.", ipCamara);
                    }
                    else
                    {
                        _logger.LogWarning("Filtrado: Evento de {ip} filtrado por Cooldown.", ipCamara);
                    }
                }
                await Task.Delay(100, stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError("Error during event loop: {error}", ex.Message);
        }
    }
}