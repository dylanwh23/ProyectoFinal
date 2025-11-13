using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Shared.Contracts.Config;
using Shared.Contracts.Models;

namespace EventProcessor.Worker.Services;

public class EventSimulatorService : BackgroundService
{
    private readonly RabbitMQConfig _config;
    private readonly ILogger<EventSimulatorService> _logger;
    private readonly List<CameraSimulation> _cameras;

    public EventSimulatorService(
        IOptions<RabbitMQConfig> config,
        ILogger<EventSimulatorService> logger)
    {
        _config = config.Value;
        _logger = logger;

        // Camaras basadas en casos de uso reales
        _cameras = new List<CameraSimulation>
        {
            // Monitoreo de Inventario en Grilla Fija
            new() {
                CameraId = "cam_inventario_1",
                CameraIP = "192.168.1.101",
                Interval = TimeSpan.FromSeconds(12),
                Zone = "Pasillo-A",
                EventType = "QR_DETECTED"
            },
            
            // Escaneo en Cinta Transportadora  
            new() {
                CameraId = "cam_cinta_1",
                CameraIP = "192.168.1.102",
                Interval = TimeSpan.FromSeconds(8),
                Zone = "Cinta-Transportadora",
                EventType = "PALLET_SCAN"
            },
            
            // Deteccion de Camiones
            new() {
                CameraId = "cam_entrada_1",
                CameraIP = "192.168.1.103",
                Interval = TimeSpan.FromSeconds(25),
                Zone = "Puerta-Principal",
                EventType = "TRUCK_DETECTED"
            },
            
            // Camara adicional para mas variedad
            new() {
                CameraId = "cam_almacen_1",
                CameraIP = "192.168.1.104",
                Interval = TimeSpan.FromSeconds(15),
                Zone = "Almacen-Central",
                EventType = "QR_DETECTED"
            }
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Starting Event Simulator Service...");

        // Esperar a que RabbitMQ Consumer se inicialice
        await Task.Delay(5000, stoppingToken);

        _logger.LogInformation("📹 Starting camera simulations...");

        var tasks = _cameras.Select(camera => SimulateCameraEvents(camera, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task SimulateCameraEvents(CameraSimulation camera, CancellationToken stoppingToken)
    {
        _logger.LogInformation("🎬 Simulating {EventType} events for {CameraId} ({IP}) in {Zone}",
            camera.EventType, camera.CameraId, camera.CameraIP, camera.Zone);

        var random = new Random();
        var eventCount = 0;

        // Simular solo 8 eventos por camara para la prueba
        while (!stoppingToken.IsCancellationRequested && eventCount < 8)
        {
            try
            {
                await Task.Delay(camera.Interval, stoppingToken);

                var cameraEvent = new EventoMovimientoDetectado
                {
                    Momento = DateTime.UtcNow,
                    IpCamara = camera.CameraIP,
                    MensajeCrudoEvento = GenerateRealisticMessage(camera, random, eventCount + 1)
                };

                PublishEventToRabbitMQ(cameraEvent);
                eventCount++;

                _logger.LogInformation("📤 [{EventType}] {CameraId}: {Message}",
                    camera.EventType, camera.CameraId, cameraEvent.MensajeCrudoEvento);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error simulating events for camera {CameraId}", camera.CameraId);
            }
        }

        _logger.LogInformation("✅ Finished simulation for {CameraId} - {Count} events sent",
            camera.CameraId, eventCount);
    }

    private string GenerateRealisticMessage(CameraSimulation camera, Random random, int eventNumber)
    {
        return camera.EventType switch
        {
            "QR_DETECTED" => GenerateQREvent(camera, random, eventNumber),
            "PALLET_SCAN" => GeneratePalletEvent(camera, random, eventNumber),
            "TRUCK_DETECTED" => GenerateTruckEvent(camera, random, eventNumber),
            _ => $"Evento #{eventNumber} en {camera.Zone} - Timestamp: {DateTime.UtcNow:HH:mm:ss}"
        };
    }

    private string GenerateQREvent(CameraSimulation camera, Random random, int eventNumber)
    {
        var qrCodes = new[] { "QR_INV_", "QR_BOX_", "QR_LOC_", "QR_PALLET_" };
        var actions = new[] { "detected", "scanned", "identified", "read" };
        var status = new[] { "OK", "VALID", "PROCESSED", "VERIFIED" };

        var qrPrefix = qrCodes[random.Next(qrCodes.Length)];
        var qrNumber = random.Next(1000, 9999);
        var action = actions[random.Next(actions.Length)];
        var stat = status[random.Next(status.Length)];

        return $"{qrPrefix}{qrNumber} {action} at {camera.Zone} - Status: {stat} - Event#{eventNumber}";
    }

    private string GeneratePalletEvent(CameraSimulation camera, Random random, int eventNumber)
    {
        var palletIds = new[] { "PALLET-A", "PALLET-B", "PALLET-C", "PALLET-D" };
        var boxCount = random.Next(5, 25);
        var conditions = new[] { "complete", "partial", "verified", "pending" };

        return $"Pallet {palletIds[random.Next(palletIds.Length)]} scanned with {boxCount} boxes - Condition: {conditions[random.Next(conditions.Length)]} - Zone: {camera.Zone} - Event#{eventNumber}";
    }

    private string GenerateTruckEvent(CameraSimulation camera, Random random, int eventNumber)
    {
        var truckIds = new[] { "TRUCK-7845", "TRUCK-9210", "TRUCK-3567", "TRUCK-1489" };
        var directions = new[] { "entering", "exiting", "parked at", "departing from" };
        var drivers = new[] { "Driver123", "Driver456", "Driver789", "Driver101" };

        return $"Truck {truckIds[random.Next(truckIds.Length)]} {directions[random.Next(directions.Length)]} {camera.Zone} - Driver: {drivers[random.Next(drivers.Length)]} - Event#{eventNumber}";
    }

    private void PublishEventToRabbitMQ(EventoMovimientoDetectado cameraEvent)
    {
        try
        {
            var factory = new ConnectionFactory()
            {
                HostName = _config.Host,
                UserName = _config.Username,
                Password = _config.Password
            };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            channel.ExchangeDeclare(exchange: _config.ExchangeName, type: ExchangeType.Direct, durable: true);

            var message = JsonSerializer.Serialize(cameraEvent);
            var body = Encoding.UTF8.GetBytes(message);

            channel.BasicPublish(
                exchange: _config.ExchangeName,
                routingKey: "",
                basicProperties: null,
                body: body);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error publishing to RabbitMQ");
        }
    }
}

public class CameraSimulation
{
    public string CameraId { get; set; } = string.Empty;
    public string CameraIP { get; set; } = string.Empty;
    public TimeSpan Interval { get; set; }
    public string Zone { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
}
