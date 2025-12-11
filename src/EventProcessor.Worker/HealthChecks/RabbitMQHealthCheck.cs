using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Shared.Contracts.Config;
using System.Threading.Channels;

namespace EventProcessor.Worker.HealthChecks;

public class RabbitMQHealthCheck : IHealthCheck, IDisposable
{
    private readonly RabbitMQConfig _config;
    private readonly ILogger<RabbitMQHealthCheck> _logger;
    private IConnection? _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public RabbitMQHealthCheck(
        IOptions<RabbitMQConfig> config,
        ILogger<RabbitMQHealthCheck> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Intentar crear una conexión si no existe
            if (_connection == null || !_connection.IsOpen)
            {
                await TryConnectAsync(cancellationToken);
            }

            if (_connection?.IsOpen == true)
            {
                using var channel = _connection.CreateModel();

                // Verificar que podemos crear un canal
                if (channel.IsOpen)
                {
                    return HealthCheckResult.Healthy(
                        "Conexión a RabbitMQ establecida correctamente");
                }
            }

            return HealthCheckResult.Unhealthy(
                "No se pudo establecer conexión con RabbitMQ");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error en health check de RabbitMQ");
            return HealthCheckResult.Unhealthy(
                "Error al conectar con RabbitMQ",
                ex);
        }
    }

    private async Task TryConnectAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_connection?.IsOpen == true) return;

            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = _config.Host,
                    UserName = _config.Username,
                    Password = _config.Password,
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
                };

                _connection = factory.CreateConnection();
                _logger.LogInformation("✅ Conexión a RabbitMQ establecida");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error al conectar con RabbitMQ");
                _connection = null;
                throw;
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.Close();
            _connection?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
