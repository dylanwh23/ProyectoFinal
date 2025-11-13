using EventProcessor.Worker.Data;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Contracts.Config;
using Shared.Contracts.Models;
using System.Text;
using System.Text.Json;

namespace EventProcessor.Worker.Services;

public class RabbitMQConsumerService : BackgroundService
{
    private readonly RabbitMQConfig _config;
    private readonly EventProcessorService _eventProcessor;
    private readonly ILogger<RabbitMQConsumerService> _logger;
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMQConsumerService(
        IOptions<RabbitMQConfig> config,
        EventProcessorService eventProcessor,
        ILogger<RabbitMQConsumerService> logger)
    {
        _config = config.Value;
        _eventProcessor = eventProcessor;
        _logger = logger;
        InitializeRabbitMQ();
    }

    private void InitializeRabbitMQ()
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
            _channel = _connection.CreateModel();

            // Declarar exchange y cola (debe coincidir con TelnetInterceptor)
            _channel.ExchangeDeclare(exchange: _config.ExchangeName, type: ExchangeType.Direct, durable: true);
            _channel.QueueDeclare(queue: _config.QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueBind(queue: _config.QueueName, exchange: _config.ExchangeName, routingKey: "");

            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            _logger.LogInformation("✅ RabbitMQ consumer initialized for queue: {QueueName}", _config.QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error initializing RabbitMQ consumer");
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Starting RabbitMQ consumer...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_connection == null || !_connection.IsOpen || _channel == null)
                {
                    _logger.LogWarning("🔄 RabbitMQ connection is closed. Reinitializing...");
                    InitializeRabbitMQ();
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                var consumer = new EventingBasicConsumer(_channel);
                consumer.Received += async (model, ea) =>
                {
                    await ProcessMessage(ea);
                };

                _channel.BasicConsume(queue: _config.QueueName, autoAck: false, consumer: consumer);

                _logger.LogInformation("✅ RabbitMQ consumer started successfully");

                // Mantener el servicio corriendo
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in RabbitMQ consumer execution");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task ProcessMessage(BasicDeliverEventArgs ea)
    {
        string message = string.Empty;

        try
        {
            /* 
             * DELAY TEMPORAL - Para ver mensajes en RabbitMQ Management
            _logger.LogInformation("⏳ Message received, waiting 5 seconds before processing...");
            await Task.Delay(5000); 
            */

            var body = ea.Body.ToArray();
            message = Encoding.UTF8.GetString(body);

            _logger.LogInformation("📨 Processing message from RabbitMQ: {Message}", message);

            var cameraEvent = JsonSerializer.Deserialize<EventoMovimientoDetectado>(message);
            if (cameraEvent == null)
            {
                _logger.LogWarning("⚠️ Failed to deserialize message: {Message}", message);
                _channel?.BasicAck(ea.DeliveryTag, false);
                return;
            }

            _logger.LogInformation("🔍 Processing event from IP: {Ip}, Time: {Time}",
                cameraEvent.IpCamara, cameraEvent.Momento);

            // Procesar el evento
            var success = await _eventProcessor.ProcessAndStoreEventAsync(cameraEvent);

            if (success)
            {
                _channel?.BasicAck(ea.DeliveryTag, false);
                _logger.LogInformation("✅ Event processed successfully - IP: {Ip}, Stored in database", cameraEvent.IpCamara);
            }
            else
            {
                _channel?.BasicNack(ea.DeliveryTag, false, true); // Requeue
                _logger.LogWarning("🔄 Event processing failed - requeued - IP: {Ip}", cameraEvent.IpCamara);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing RabbitMQ message: {Message}", message);
            _channel?.BasicNack(ea.DeliveryTag, false, false); // No requeue - mensaje problematico
        }
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
