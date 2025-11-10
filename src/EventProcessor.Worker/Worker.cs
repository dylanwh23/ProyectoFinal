namespace EventProcessor.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 EventProcessor Worker started at: {time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            // El trabajo real lo hace RabbitMQConsumerService
            // Este worker solo mantiene el servicio vivo y muestra heartbeat
            await Task.Delay(10000, stoppingToken);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("💚 EventProcessor Worker heartbeat at: {time}", DateTimeOffset.Now);
            }
        }
    }
}
