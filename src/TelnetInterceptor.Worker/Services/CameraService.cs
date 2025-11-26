using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

public class CameraStreamService : IHostedService, IDisposable
{
    private readonly ILogger<CameraStreamService> _logger;
    private string _baseWatchPath;
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, string> _latestFilePerCamera = new();
    private readonly SemaphoreSlim _watcherLock = new(1, 1);

    private const int HistoryReportLimit = 500;
    private const int HistoryKeepLimit = 550;

    private Timer? _cleanupTimer;

    public int GetReportLimit() => HistoryReportLimit;

    public CameraStreamService(ILogger<CameraStreamService> logger, IOptions<ServerSettings> settings)
    {
        _logger = logger;
        _baseWatchPath = settings.Value.WatchPath;
    }

    public string GetWatchPath() => _baseWatchPath;

    public string GetCameraPath(string cameraName)
    {
        return Path.Combine(_baseWatchPath, cameraName);
    }

    public async Task ActualizarRutaBase(string nuevaRuta)
    {
        await _watcherLock.WaitAsync();
        try
        {
            _logger.LogInformation("🔄 Actualizando ruta base de {rutaAntigua} a {rutaNueva}", _baseWatchPath, nuevaRuta);

            // Detener el watcher actual
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            // Actualizar la ruta
            _baseWatchPath = nuevaRuta;

            // Crear el directorio si no existe
            if (!Directory.Exists(_baseWatchPath))
            {
                Directory.CreateDirectory(_baseWatchPath);
            }

            // Reiniciar el watcher con la nueva ruta
            _watcher = new FileSystemWatcher(_baseWatchPath)
            {
                Filter = "*.bmp",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFileEvent;

            // Limpiar el caché de archivos más recientes
            _latestFilePerCamera.Clear();

            _logger.LogInformation("✅ Ruta base actualizada y watcher reiniciado correctamente");
        }
        finally
        {
            _watcherLock.Release();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Iniciando Camera Stream Service en: {path}", _baseWatchPath);
        if (!Directory.Exists(_baseWatchPath)) Directory.CreateDirectory(_baseWatchPath);

        _watcher = new FileSystemWatcher(_baseWatchPath)
        {
            Filter = "*.bmp",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnFileEvent;
        _cleanupTimer = new Timer(DoCleanup, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        string? cameraName = Path.GetDirectoryName(e.FullPath)?.Split(Path.DirectorySeparatorChar).LastOrDefault();
        if (cameraName != null)
        {
            _latestFilePerCamera[cameraName] = e.FullPath;
        }
    }

    public HistorySnapshot FreezeHistory(string cameraName)
    {
        string cameraPath = GetCameraPath(cameraName);
        string historyId = $"history-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        string historyPath = Path.Combine(cameraPath, historyId);
        Directory.CreateDirectory(historyPath);
        var dirInfo = new DirectoryInfo(cameraPath);
        var filesToMove = dirInfo.GetFiles("*.bmp", SearchOption.TopDirectoryOnly)
                                 .OrderBy(f => f.CreationTimeUtc)
                                 .TakeLast(HistoryReportLimit)
                                 .ToList();
        var movedFileNames = new List<string>();
        foreach (var file in filesToMove)
        {
            try
            {
                file.CopyTo(Path.Combine(historyPath, file.Name));
                movedFileNames.Add(file.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo mover {file} al historial", file.FullName);
            }
        }
        _logger.LogInformation("Historial congelado en {id} con {count} archivos.", historyId, movedFileNames.Count);
        return new HistorySnapshot { HistoryId = historyId, Files = movedFileNames };
    }

    private void DoCleanup(object? state)
    {
        _logger.LogInformation("Iniciando limpieza periódica...");
        try
        {
            var cameraDirectories = Directory.GetDirectories(_baseWatchPath);
            foreach (var camDir in cameraDirectories)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(camDir);
                    var liveFiles = dirInfo.GetFiles("*.bmp", SearchOption.TopDirectoryOnly).OrderBy(f => f.CreationTimeUtc).ToList();
                    if (liveFiles.Count > HistoryKeepLimit)
                    {
                        int filesToDeleteCount = liveFiles.Count - HistoryKeepLimit;
                        var filesToDelete = liveFiles.Take(filesToDeleteCount).ToList();
                        _logger.LogInformation("Limpiando {count} archivos de {cam} (en vivo)", filesToDelete.Count, dirInfo.Name);
                        foreach (var file in filesToDelete) { try { file.Delete(); } catch { } }
                    }
                    var historyDirs = dirInfo.GetDirectories("history-*", SearchOption.TopDirectoryOnly);
                    var cutoffDate = DateTime.UtcNow.AddMinutes(-5);
                    foreach (var historyDir in historyDirs)
                    {
                        if (historyDir.CreationTimeUtc < cutoffDate)
                        {
                            _logger.LogInformation("Borrando historial antiguo (fallback): {dir}", historyDir.Name);
                            try { historyDir.Delete(recursive: true); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error limpiando la carpeta {camDir}", camDir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error grave en la tarea DoCleanup");
        }
    }

    public void CleanupHistoryFolder(string cameraName, string historyId)
    {
        _logger.LogInformation("Solicitud de borrado para historial: {id}", historyId);

        string safeHistoryId = Path.GetFileName(historyId);
        if (string.IsNullOrEmpty(safeHistoryId) || !safeHistoryId.StartsWith("history-"))
        {
            _logger.LogWarning("Solicitud de borrado rechazada (nombre inválido): {id}", historyId);
            return;
        }

        string cameraPath = GetCameraPath(cameraName);
        string historyPath = Path.Combine(cameraPath, safeHistoryId);

        try
        {
            if (Directory.Exists(historyPath))
            {
                Directory.Delete(historyPath, recursive: true);
                _logger.LogInformation("Historial {id} borrado exitosamente.", safeHistoryId);
            }
            else
            {
                _logger.LogWarning("No se encontró el historial a borrar: {id}", safeHistoryId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al borrar la carpeta de historial: {path}", historyPath);
        }
    }

    public HistorySnapshot FreezeHistoryByTimeRangeLocal(string cameraName, DateTime startTime, DateTime endTime)
    {
        _logger.LogInformation("Congelando historial por HORA LOCAL de {start} a {end}", startTime, endTime);
        string cameraPath = GetCameraPath(cameraName);
        string historyId = $"history-event-local-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        string historyPath = Path.Combine(cameraPath, historyId);
        Directory.CreateDirectory(historyPath);

        var dirInfo = new DirectoryInfo(cameraPath);
        var filesForEvent = dirInfo.GetFiles("*.bmp", SearchOption.TopDirectoryOnly)
                                   .Where(f => f.CreationTime >= startTime && f.CreationTime <= endTime)
                                   .OrderBy(f => f.CreationTime)
                                   .ToList();

        var movedFileNames = new List<string>();
        foreach (var file in filesForEvent)
        {
            try
            {
                file.CopyTo(Path.Combine(historyPath, file.Name));
                movedFileNames.Add(file.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo copiar {file} al historial del evento local", file.FullName);
            }
        }

        _logger.LogInformation("Historial de evento local congelado en {id} con {count} archivos.", historyId, movedFileNames.Count);
        return new HistorySnapshot { HistoryId = historyId, Files = movedFileNames };
    }

    public HistorySnapshot FreezeHistoryByNumberRange(string cameraName, int desde, int hasta)
    {
        _logger.LogInformation("Congelando historial por rango de números: {desde} a {hasta}", desde, hasta);
        string cameraPath = GetCameraPath(cameraName);
        string historyId = $"history-range-{desde}-{hasta}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        string historyPath = Path.Combine(cameraPath, historyId);
        Directory.CreateDirectory(historyPath);

        var dirInfo = new DirectoryInfo(cameraPath);

        // Buscar archivos que coincidan con el patrón Snapshot*_NUMBER.bmp
        var allFiles = dirInfo.GetFiles("*.bmp", SearchOption.TopDirectoryOnly);
        var filesInRange = new List<(FileInfo file, int number)>();

        foreach (var file in allFiles)
        {
            // Extraer el número del nombre del archivo
            // Patrón: Snapshot1_204.bmp -> extraer 204
            var fileName = Path.GetFileNameWithoutExtension(file.Name);
            var parts = fileName.Split('_');

            if (parts.Length >= 2 && int.TryParse(parts[^1], out int snapshotNumber))
            {
                if (snapshotNumber >= desde && snapshotNumber <= hasta)
                {
                    filesInRange.Add((file, snapshotNumber));
                }
            }
        }

        // Ordenar por número de snapshot
        var sortedFiles = filesInRange.OrderBy(x => x.number).Select(x => x.file).ToList();

        var movedFileNames = new List<string>();
        foreach (var file in sortedFiles)
        {
            try
            {
                file.CopyTo(Path.Combine(historyPath, file.Name));
                movedFileNames.Add(file.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo copiar {file} al historial", file.FullName);
            }
        }

        _logger.LogInformation(
            "Historial por rango congelado en {id} con {count} archivos (de {desde} a {hasta}).",
            historyId, movedFileNames.Count, desde, hasta);

        return new HistorySnapshot { HistoryId = historyId, Files = movedFileNames };
    }

    public string? GetLatestFileForCamera(string cameraName)
    {
        _latestFilePerCamera.TryGetValue(cameraName, out var path);
        return path;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deteniendo servicio.");
        _watcher?.Dispose();
        _cleanupTimer?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _cleanupTimer?.Dispose();
        _watcherLock?.Dispose();
    }
}

public class HistorySnapshot
{
    public string HistoryId { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new List<string>();
}

public class ServerSettings
{
    public string WatchPath { get; set; } = "C:\\Public";
}