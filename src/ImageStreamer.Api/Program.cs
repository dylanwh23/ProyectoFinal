using Microsoft.Extensions.FileProviders;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// --- Configuración ---
builder.Services.Configure<ServerSettings>(builder.Configuration.GetSection("ServerSettings"));
builder.Services.AddSingleton<CameraStreamService>();
builder.Services.AddHostedService(p => p.GetRequiredService<CameraStreamService>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// --- API de STREAM (MJPG) ---
app.MapGet("/api/stream/{cameraName}", async (
    string cameraName, CameraStreamService cameraService, HttpContext http, CancellationToken ct) =>
{
    http.Response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
    while (!ct.IsCancellationRequested)
    {
        string? latestFile = cameraService.GetLatestFileForCamera(cameraName);
        if (latestFile != null && File.Exists(latestFile))
        {
            byte[]? jpegData = await ProcessImageAsync(latestFile, cameraService, null); // null historyId
            if (jpegData != null)
            {
                await http.Response.WriteAsync("--frame\r\n", ct);
                await http.Response.WriteAsync("Content-Type: image/jpeg\r\n", ct);
                await http.Response.WriteAsync($"Content-Length: {jpegData.Length}\r\n\r\n", ct);
                await http.Response.Body.WriteAsync(jpegData, ct);
                await http.Response.WriteAsync("\r\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }
        }
        await Task.Delay(50, ct);
    }
});

// --- API de FRAME (Última foto O foto específica) ---
app.MapGet("/api/frame/{cameraName}", async (
    string cameraName, CameraStreamService cameraService, HttpContext http) =>
{
    string? specificFile = http.Request.Query["file"];
    string? historyId = http.Request.Query["historyId"];
    string fileToProcess;
    if (!string.IsNullOrEmpty(specificFile) && !string.IsNullOrEmpty(historyId))
    {
        string safeHistoryId = Path.GetFileName(historyId);
        string safeFileName = Path.GetFileName(specificFile);
        fileToProcess = Path.Combine(cameraService.GetCameraPath(cameraName), safeHistoryId, safeFileName);
        if (!File.Exists(fileToProcess))
        {
            http.Response.StatusCode = 404;
            await http.Response.WriteAsync("El archivo de historial ya no existe.");
            return;
        }
    }
    else
    {
        fileToProcess = cameraService.GetLatestFileForCamera(cameraName);
        if (fileToProcess == null || !File.Exists(fileToProcess))
        {
            http.Response.StatusCode = 404;
            await http.Response.WriteAsync("No se ha detectado ninguna imagen en vivo.");
            return;
        }
    }
    byte[]? jpegData = await ProcessImageAsync(fileToProcess, cameraService, historyId);
    if (jpegData == null)
    {
        http.Response.StatusCode = 500;
        await http.Response.WriteAsync("Error al procesar la imagen.");
        return;
    }
    http.Response.ContentType = "image/jpeg";
    http.Response.ContentLength = jpegData.Length;
    await http.Response.Body.WriteAsync(jpegData);
});

// --- API para "Congelar" el historial ---
app.MapGet("/api/history/freeze/{cameraName}", (string cameraName, CameraStreamService cameraService) =>
{
    try
    {
        return Results.Ok(cameraService.FreezeHistory(cameraName));
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: $"Error al congelar el historial: {ex.Message}", statusCode: 500);
    }
});

// --- NUEVA: API para Borrar el historial ---
app.MapDelete("/api/history/cleanup/{cameraName}/{historyId}", (
    string cameraName,
    string historyId,
    CameraStreamService cameraService) =>
{
    try
    {
        cameraService.CleanupHistoryFolder(cameraName, historyId);
        return Results.Ok(new { message = "Historial borrado" });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: $"Error al borrar historial: {ex.Message}", statusCode: 500);
    }
});


// --- Función de ayuda para procesar imágenes ---
async Task<byte[]?> ProcessImageAsync(string filePath, CameraStreamService cameraService, string? historyId)
{
    for (int i = 0; i < 5; i++)
    {
        try
        {
            using var image = await Image.LoadAsync(filePath);
            image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(1280, 720) }));
            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85 });
            return ms.ToArray();
        }
        catch (IOException)
        {
            await Task.Delay(100);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error procesando {filePath}: {ex.Message}");
            return null;
        }
    }
    return null;
}

app.MapFallbackToFile("index.html");
app.Run();

// --- El Servicio de Cámara ---
public class CameraStreamService : IHostedService, IDisposable
{
    private readonly ILogger<CameraStreamService> _logger;
    private readonly string _baseWatchPath;
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, string> _latestFilePerCamera = new();

    // --- Límites de historial ---
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
        _logger.LogInformation("Iniciando limpieza periódica...");
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

    // --- NUEVO: Método para borrar una carpeta de historial específica ---
    public void CleanupHistoryFolder(string cameraName, string historyId)
    {
        _logger.LogInformation("Solicitud de borrado para historial: {id}", historyId);

        // --- Medidas de seguridad ---
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

// --- Clases de Configuración ---
public class ServerSettings { public string WatchPath { get; set; } = "C:\\Public"; }
public class HistorySnapshot { public string HistoryId { get; set; } = string.Empty; public List<string> Files { get; set; } = new List<string>(); }