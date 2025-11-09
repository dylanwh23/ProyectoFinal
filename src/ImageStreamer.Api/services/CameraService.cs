using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
public class CameraStreamService : IHostedService, IDisposable
{
    private readonly ILogger<CameraStreamService> _logger;
    private readonly string _baseWatchPath;
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, string> _latestFilePerCamera = new();

    // --- L�mites de historial ---
    private const int HistoryReportLimit = 200; // 200 para la API
    private const int HistoryKeepLimit = 250;   // 250 en disco (200 + 50 buffer)

    private Timer? _cleanupTimer;

    public int GetReportLimit() => HistoryReportLimit;

    public CameraStreamService(ILogger<CameraStreamService> logger, IOptions<ServerSettings> settings)
    {
        _logger = logger;
        _baseWatchPath = settings.Value.WatchPath;
    }

    public string GetCameraPath(string cameraName)
    {
        return Path.Combine(_baseWatchPath, cameraName);
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
        _logger.LogInformation("Iniciando limpieza peri�dica...");
        try
        {
            var cameraDirectories = Directory.GetDirectories(_baseWatchPath);
            foreach (var camDir in cameraDirectories)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(camDir);
                    // 1. Limpieza "EN VIVO"
                    var liveFiles = dirInfo.GetFiles("*.bmp", SearchOption.TopDirectoryOnly).OrderBy(f => f.CreationTimeUtc).ToList();
                    if (liveFiles.Count > HistoryKeepLimit)
                    {
                        int filesToDeleteCount = liveFiles.Count - HistoryKeepLimit;
                        var filesToDelete = liveFiles.Take(filesToDeleteCount).ToList();
                        _logger.LogInformation("Limpiando {count} archivos de {cam} (en vivo)", filesToDelete.Count, dirInfo.Name);
                        foreach (var file in filesToDelete) { try { file.Delete(); } catch { /* ignorar */ } }
                    }
                    // 2. Limpieza de carpetas "CONGELADAS" viejas (el fallback de 5 min)
                    var historyDirs = dirInfo.GetDirectories("history-*", SearchOption.TopDirectoryOnly);
                    var cutoffDate = DateTime.UtcNow.AddMinutes(-5);
                    foreach (var historyDir in historyDirs)
                    {
                        if (historyDir.CreationTimeUtc < cutoffDate)
                        {
                            _logger.LogInformation("Borrando historial antiguo (fallback): {dir}", historyDir.Name);
                            try { historyDir.Delete(recursive: true); } catch { /* ignorar */ }
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

    // --- NUEVO: M�todo para borrar una carpeta de historial espec�fica ---
    public void CleanupHistoryFolder(string cameraName, string historyId)
    {
        _logger.LogInformation("Solicitud de borrado para historial: {id}", historyId);

        // --- Medidas de seguridad ---
        string safeHistoryId = Path.GetFileName(historyId);
        if (string.IsNullOrEmpty(safeHistoryId) || !safeHistoryId.StartsWith("history-"))
        {
            _logger.LogWarning("Solicitud de borrado rechazada (nombre inv�lido): {id}", historyId);
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
                _logger.LogWarning("No se encontr� el historial a borrar: {id}", safeHistoryId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al borrar la carpeta de historial: {path}", historyPath);
        }
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
    }
}