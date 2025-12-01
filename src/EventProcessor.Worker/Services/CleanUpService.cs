using Microsoft.Extensions.Options;

namespace EventProcessor.Worker.Services;

public class CleanupService : BackgroundService
{
    private readonly ILogger<CleanupService> _logger;
    private readonly string _jsonFolderPath;
    private readonly TimeSpan _retentionPeriod;
    private readonly TimeSpan _cleanupInterval;

    public CleanupService(IConfiguration configuration, ILogger<CleanupService> logger)
    {
        _logger = logger;
        _jsonFolderPath = configuration["JsonExport:FolderPath"] ?? "./EventJsonExports";

        // Configuración: 7 días de retención, limpieza cada 24 horas
        _retentionPeriod = TimeSpan.FromDays(7);
        _cleanupInterval = TimeSpan.FromHours(24);

        _logger.LogInformation("[] Servicio de limpieza configurado - Retención: {Dias} días, Intervalo: {Horas} horas",
            _retentionPeriod.Days, _cleanupInterval.TotalHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[] Iniciando servicio de limpieza automática de JSONs...");

        // Esperar un poco al inicio para que todo esté estable
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RealizarLimpiezaAsync();
                _logger.LogInformation(">> Limpieza completada. Próxima en {Horas} horas", _cleanupInterval.TotalHours);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "!! Error durante la limpieza automática");
            }

            // Esperar hasta la próxima limpieza
            await Task.Delay(_cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("[] Servicio de limpieza finalizado");
    }

    private async Task RealizarLimpiezaAsync()
    {
        if (!Directory.Exists(_jsonFolderPath))
        {
            _logger.LogWarning("[] Carpeta de JSONs no encontrada: {Ruta}", _jsonFolderPath);
            return;
        }

        var fechaLimite = DateTime.Now - _retentionPeriod;
        var archivos = Directory.GetFiles(_jsonFolderPath, "*.json");

        _logger.LogInformation("[] Buscando archivos JSON anteriores a: {FechaLimite}", fechaLimite);

        var archivosAEliminar = new List<string>();
        var espacioLiberar = 0L;

        foreach (var archivo in archivos)
        {
            var infoArchivo = new FileInfo(archivo);
            if (infoArchivo.LastWriteTime < fechaLimite)
            {
                archivosAEliminar.Add(archivo);
                espacioLiberar += infoArchivo.Length;
            }
        }

        if (archivosAEliminar.Count == 0)
        {
            _logger.LogInformation("[] No hay archivos para eliminar. Total actual: {TotalArchivos}", archivos.Length);
            return;
        }

        _logger.LogInformation("[] Eliminando {Cantidad} archivos JSON antiguos. Espacio a liberar: {Espacio} MB",
            archivosAEliminar.Count, (espacioLiberar / 1024.0 / 1024.0).ToString("F2"));

        // Eliminar archivos
        var eliminadosExitosos = 0;
        foreach (var archivo in archivosAEliminar)
        {
            try
            {
                File.Delete(archivo);
                eliminadosExitosos++;
                _logger.LogDebug(">> Eliminado: {Archivo}", Path.GetFileName(archivo));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "!! No se pudo eliminar: {Archivo}", archivo);
            }
        }

        _logger.LogInformation("[] Limpieza finalizada: {Eliminados}/{Total} archivos eliminados. Espacio liberado: ~{Espacio} MB",
            eliminadosExitosos, archivosAEliminar.Count, (espacioLiberar / 1024.0 / 1024.0).ToString("F2"));
    }
}
