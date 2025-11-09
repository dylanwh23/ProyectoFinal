using Microsoft.Extensions.Options;
using System.IO;

namespace ImageInterceptor.Worker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly string _watchPath;
        private readonly int _daysToKeep;
        private FileSystemWatcher? _watcher;

        public Worker(
            ILogger<Worker> logger,
            IOptions<ImageWatcherSettings> watcherSettings,
            IOptions<CleanupSettings> cleanupSettings)
        {
            _logger = logger;
            _watchPath = watcherSettings.Value.WatchPath;
            _daysToKeep = cleanupSettings.Value.DaysToKeep;

            if (string.IsNullOrEmpty(_watchPath) || !Directory.Exists(_watchPath))
            {
                _logger.LogCritical("Ruta inválida: {path}", _watchPath);
            }
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Iniciando vigilancia de carpeta: {path}", _watchPath);

            if (string.IsNullOrEmpty(_watchPath) || !Directory.Exists(_watchPath))
                return Task.CompletedTask;

            _watcher = new FileSystemWatcher(_watchPath)
            {
                Filter = "*.bmp",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileCreated;
            return base.StartAsync(cancellationToken);
        }

        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            _logger.LogInformation("Nueva imagen detectada: {path}", e.FullPath);
            bool success = false;
            int retries = 5;

            for (int i = 1; i <= retries; i++)
            {
                try
                {
                    await Task.Delay(200 * i);
                    if (!File.Exists(e.FullPath)) return;

                    var fileInfo = new FileInfo(e.FullPath);
                    string timestamp = fileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd_HH-mm-ss-fff");
                    string newPath = Path.Combine(fileInfo.DirectoryName!, $"{timestamp}{fileInfo.Extension}");
                    File.Move(fileInfo.FullName, newPath);
                    _logger.LogInformation("Renombrado a: {newPath}", newPath);
                    success = true;
                    break;
                }
                catch (IOException)
                {
                    _logger.LogWarning("Intento {i} fallido (archivo en uso): {path}", i, e.FullPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al renombrar: {path}", e.FullPath);
                    break;
                }
            }

            if (!success)
                _logger.LogError("No se pudo renombrar el archivo: {path}", e.FullPath);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Iniciando limpieza cada hora, conservando {days} días", _daysToKeep);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    PerformCleanup();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en limpieza periódica");
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private void PerformCleanup()
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-_daysToKeep);
            var files = Directory.GetFiles(_watchPath, "*.bmp", SearchOption.AllDirectories);
            int deleted = 0;

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoffDate)
                    {
                        info.Delete();
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error al eliminar {file}", file);
                }
            }

            _logger.LogInformation("Limpieza completada. {count} archivos eliminados", deleted);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Deteniendo servicio.");
            _watcher?.Dispose();
            return base.StopAsync(cancellationToken);
        }
    }
}
