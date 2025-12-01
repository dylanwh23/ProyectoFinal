using Microsoft.Extensions.Hosting;

namespace EventProcessor.Worker;

public class Worker(ILogger<Worker> logger, IHostApplicationLifetime appLifetime) : BackgroundService
{
    private readonly ILogger<Worker> _logger = logger;
    private readonly IHostApplicationLifetime _appLifetime = appLifetime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(">> Worker de EventProcessor iniciado a las: {tiempo}", DateTimeOffset.Now);

        // Registrar eventos de ciclo de vida de la aplicación
        _appLifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("[] Aplicación recibió señal de detención...");
            _logger.LogInformation("[] Finalizando servicios y liberando recursos...");
        });

        _appLifetime.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation(">> Aplicación completamente detenida");
            _logger.LogInformation("[] Resumen final - Procesamiento completado");

            // Pausar antes de cerrar completamente
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                Console.WriteLine("\n Presiona ENTER para cerrar la ventana...");
                Console.ReadLine();
            });
        });

        try
        {
            _logger.LogInformation(">> Worker inicializado correctamente");
            _logger.LogInformation("[] Esperando eventos de cámaras...");
            _logger.LogInformation("[] API disponible en: http://localhost:5005");

            var contadorLatidos = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                // El trabajo real lo hace RabbitMQConsumerService
                // Este worker solo mantiene el servicio vivo y muestra latido
                await Task.Delay(10000, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    contadorLatidos++;

                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation(">> Latido del Worker #{contador} a las: {tiempo}",
                            contadorLatidos, DateTimeOffset.Now);

                        // Cada 6 latidos (1 minuto), mostrar estado general
                        if (contadorLatidos % 6 == 0)
                        {
                            _logger.LogInformation("[] Sistema funcionando correctamente - {minutos} minuto(s) de operación",
                                contadorLatidos / 6);
                        }
                    }
                }
            }

            _logger.LogInformation("[] Worker finalizando ejecución principal");
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("[]  Worker cancelado por solicitud");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "!! Error crítico en el Worker");
        }
        finally
        {
            _logger.LogInformation("[] Liberando recursos del Worker...");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[] Iniciando parada controlada del Worker...");

        try
        {
            await base.StopAsync(cancellationToken);
            _logger.LogInformation("[] Parada controlada completada");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[] Error durante la parada controlada");
        }
    }
}
