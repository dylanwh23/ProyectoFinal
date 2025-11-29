using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Contracts.Config;
using Shared.Contracts.Models;
using System.Text;
using System.Text.Json;

namespace EventProcessor.Worker.Services;

public class DynamicRabbitMQConsumerService : BackgroundService
{
    private readonly RabbitMQConfig _config;
    private readonly EventProcessorService _eventProcessor;
    private readonly CameraDiscoveryService _cameraDiscovery;
    private readonly ILogger<DynamicRabbitMQConsumerService> _logger;
    private IConnection? _connection;
    private readonly List<ChannelInfo> _channels = [];
    private readonly Timer _refreshTimer;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DynamicRabbitMQConsumerService(
        IOptions<RabbitMQConfig> config,
        EventProcessorService eventProcessor,
        CameraDiscoveryService cameraDiscovery,
        ILogger<DynamicRabbitMQConsumerService> logger)
    {
        _config = config.Value;
        _eventProcessor = eventProcessor;
        _cameraDiscovery = cameraDiscovery;
        _logger = logger;

        // Timer para refrescar las cámaras cada 30 segundos
        _refreshTimer = new Timer(async _ => await RefreshConsumers(), null, Timeout.Infinite, Timeout.Infinite);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Iniciando Consumidor Dinámico de RabbitMQ...");

        await InitializeRabbitMQConnection();

        // Cargar consumidores iniciales
        await RefreshConsumers();

        // Iniciar timer para refrescar cada 30 segundos
        _refreshTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(30));

        // Mantener el servicio corriendo
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        // Limpiar
        _refreshTimer?.Dispose();
        await Cleanup();
    }

    private async Task InitializeRabbitMQConnection()
    {
        try
        {
            var factory = new ConnectionFactory()
            {
                HostName = _config.Host,
                UserName = _config.Username,
                Password = _config.Password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _connection = factory.CreateConnection();
            _logger.LogInformation("✅ Conexión establecida con RabbitMQ");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error al inicializar la conexión con RabbitMQ");
            throw;
        }
    }

    private async Task RefreshConsumers()
    {
        try
        {
            if (_connection == null || !_connection.IsOpen)
            {
                _logger.LogWarning("🔄 La conexión con RabbitMQ está cerrada. Reinicializando...");
                await InitializeRabbitMQConnection();
            }

            var activeQueues = await _cameraDiscovery.GetActiveQueueNamesAsync();
            _logger.LogInformation("🔄 Actualizando consumidores para {Count} colas: {Queues}",
                activeQueues.Count, string.Join(", ", activeQueues));

            // Eliminar consumidores para colas que ya no están activas
            CleanupStaleConsumers(activeQueues);

            // Agregar consumidores para nuevas colas
            foreach (var queueName in activeQueues)
            {
                if (!_channels.Any(ch => ch.QueueName == queueName))
                {
                    await StartQueueConsumer(queueName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error actualizando consumidores");
        }
    }

    private void CleanupStaleConsumers(List<string> activeQueues)
    {
        var channelsToRemove = _channels
            .Where(ch => !activeQueues.Contains(ch.QueueName))
            .ToList();

        foreach (var channelInfo in channelsToRemove)
        {
            _logger.LogInformation("🗑️ Eliminando consumidor de la cola: {Queue}", channelInfo.QueueName);
            channelInfo.Channel.Close();
            channelInfo.Channel.Dispose();
            _channels.Remove(channelInfo);
        }
    }

    private async Task StartQueueConsumer(string queueName)
    {
        try
        {
            if (_connection == null) return;

            var channel = _connection.CreateModel();

            // Declarar exchange y cola
            channel.ExchangeDeclare(exchange: _config.ExchangeName, type: ExchangeType.Direct, durable: true);
            channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            channel.QueueBind(queue: queueName, exchange: _config.ExchangeName, routingKey: "");
            channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += async (model, ea) =>
            {
                await ProcessMessage(ea, queueName);
            };

            channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
            _channels.Add(new ChannelInfo { Channel = channel, QueueName = queueName });

            _logger.LogInformation("✅ Consumidor iniciado para la cola: {QueueName}", queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error iniciando consumidor para la cola: {QueueName}", queueName);
        }
    }

    private async Task ProcessMessage(BasicDeliverEventArgs ea, string queueName)
    {
        string message = string.Empty;

        try
        {
            var body = ea.Body.ToArray();
            message = Encoding.UTF8.GetString(body);

            _logger.LogInformation("📨 Procesando mensaje de la cola {Queue}: {Message}", queueName, message);

            var envelope = JsonSerializer.Deserialize<JsonElement>(message);

            if (envelope.TryGetProperty("message", out var messageProperty))
            {
                var cameraEvent = messageProperty.Deserialize<EventoMovimientoDetectado>(_jsonOptions);

                if (cameraEvent != null)
                {
                    _logger.LogInformation("🔍 Procesando evento de la IP: {Ip}, Cola: {Queue}",
                        cameraEvent.IpCamara, queueName);

                    var success = await _eventProcessor.ProcessAndStoreEventAsync(cameraEvent);

                    if (success)
                    {
                        GetChannelByQueue(queueName)?.BasicAck(ea.DeliveryTag, false);
                        _logger.LogInformation("✅ Evento procesado exitosamente - IP: {Ip}", cameraEvent.IpCamara);
                    }
                    else
                    {
                        GetChannelByQueue(queueName)?.BasicNack(ea.DeliveryTag, false, true);
                        _logger.LogWarning("🔄 Falló el procesamiento del evento - reencolado - IP: {Ip}", cameraEvent.IpCamara);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error procesando mensaje de la cola {Queue}: {Message}", queueName, message);
            GetChannelByQueue(queueName)?.BasicNack(ea.DeliveryTag, false, false);
        }
    }

    private IModel? GetChannelByQueue(string queueName)
    {
        return _channels.FirstOrDefault(ch => ch.QueueName == queueName)?.Channel;
    }

    private async Task Cleanup()
    {
        foreach (var channelInfo in _channels)
        {
            channelInfo?.Channel?.Close();
            channelInfo?.Channel?.Dispose();
        }
        _connection?.Close();

        _logger.LogInformation("🧹 Limpieza completada de consumidores RabbitMQ");
    }

    public override void Dispose()
    {
        _refreshTimer?.Dispose();
        Cleanup().Wait(5000);

        GC.SuppressFinalize(this);

        base.Dispose();

        _logger.LogInformation("🔚 Consumidor Dinámico de RabbitMQ finalizado");
    }

    private class ChannelInfo
    {
        public required IModel Channel { get; set; }
        public required string QueueName { get; set; }
    }
}
