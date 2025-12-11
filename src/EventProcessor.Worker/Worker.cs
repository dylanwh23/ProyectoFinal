using EventProcessor.Worker.Services;
using EventProcessor.Worker.Services.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace EventProcessor.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly EventProcessorOptions _options;
    private readonly DynamicRabbitMQConsumerService? _rabbitMQService;
    private readonly SimpleHttpServerService? _httpServer;

    private int _heartbeatCount = 0;
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(10);
    private readonly Stopwatch _uptimeStopwatch = Stopwatch.StartNew();

    public Worker(
        ILogger<Worker> logger,
        IHostApplicationLifetime appLifetime,
        IOptions<EventProcessorOptions> options,
        DynamicRabbitMQConsumerService? rabbitMQService = null,
        SimpleHttpServerService? httpServer = null)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _options = options.Value;
        _rabbitMQService = rabbitMQService;
        _httpServer = httpServer;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 Iniciando EventProcessor Worker...");
        _logger.LogInformation("📅 Hora de inicio: {Fecha}", DateTimeOffset.Now);
        _logger.LogInformation("🔌 API disponible en http://localhost:{Port}", _options.JsonExportHttpPort);
        _logger.LogInformation("📊 Métricas disponibles en http://localhost:{Port}/metrics", _options.JsonExportHttpPort);
        _logger.LogInformation("🏥 Health checks en http://localhost:{Port}/health", _options.JsonExportHttpPort);

        RegisterLifecycleEvents();

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("✅ Worker ejecutándose. Esperando eventos de cámaras...");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_heartbeatInterval, stoppingToken);

                _heartbeatCount++;
                MetricsRegistry.WorkerHeartbeat.Set(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                // Registrar métricas periódicas
                await LogHeartbeatAsync();

                // Cada 6 latidos (1 minuto), mostrar estado del sistema
                if (_heartbeatCount % 6 == 0)
                {
                    await LogSystemStatusAsync();
                }
            }

            _logger.LogInformation("⏹️ Señal de cancelación recibida. Finalizando worker...");
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("⏹️ Worker cancelado (TaskCanceledException).");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "💥 Error crítico en el Worker");
            throw;
        }
        finally
        {
            _logger.LogInformation("🧹 Finalizando ejecución del Worker y liberando recursos...");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 Iniciando parada controlada del Worker...");

        try
        {
            await base.StopAsync(cancellationToken);

            // Mostrar resumen de ejecución
            _logger.LogInformation("========================================");
            _logger.LogInformation("📈 RESUMEN DE EJECUCIÓN");
            _logger.LogInformation("========================================");
            _logger.LogInformation("⏱️  Tiempo total de ejecución: {Uptime}", _uptimeStopwatch.Elapsed);
            _logger.LogInformation("💓 Total de latidos: {Heartbeats}", _heartbeatCount);
            _logger.LogInformation("========================================");

            _logger.LogInformation("✅ Worker detenido correctamente.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error durante la parada controlada del Worker");
        }
    }

    private async Task LogHeartbeatAsync()
    {
        var uptime = _uptimeStopwatch.Elapsed;

        _logger.LogDebug(
            "💓 Latido #{Latido} · Uptime: {Uptime} · Hora: {Hora}",
            _heartbeatCount,
            $"{uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}",
            DateTimeOffset.Now.ToString("HH:mm:ss"));
    }

    private async Task LogSystemStatusAsync()
    {
        var minutesRunning = _heartbeatCount / 6;

        _logger.LogInformation(
            "📊 Estado del sistema · {Mins} min en operación · Uptime: {Uptime}",
            minutesRunning,
            _uptimeStopwatch.Elapsed);

        // Aquí podrías agregar más estadísticas del sistema
        // Ej: eventos procesados, conexiones activas, etc.
    }

    private void RegisterLifecycleEvents()
    {
        _appLifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("⏳ ApplicationStopping: preparando apagado...");
            _uptimeStopwatch.Stop();
        });

        _appLifetime.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation("✅ ApplicationStopped: Worker detenido completamente.");
        });

        _appLifetime.ApplicationStarted.Register(() =>
        {
            _logger.LogInformation("🚀 ApplicationStarted: sistema operativo en marcha.");
        });
    }

    public override void Dispose()
    {
        _uptimeStopwatch.Stop();
        GC.SuppressFinalize(this);
        base.Dispose();
    }
}
