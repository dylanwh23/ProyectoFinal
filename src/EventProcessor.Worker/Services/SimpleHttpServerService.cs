using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Prometheus;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace EventProcessor.Worker.Services;

// Contexto de serialización para las respuestas HTTP
[JsonSerializable(typeof(FileListResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthCheckResponse))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class HttpApiJsonContext : JsonSerializerContext
{
}

// DTOs para las respuestas
public record FileListResponse(int TotalArchivos, List<string> Archivos, DateTime Timestamp);
public record StatusResponse(string Estado, int TotalArchivosJson, IEnumerable<string> UltimosArchivos, DateTime Timestamp);
public record ErrorResponse(string Error);
public record HealthCheckResponse(string Status, Dictionary<string, string> Results, DateTime Timestamp);

public class SimpleHttpServerService : BackgroundService
{
    private readonly JsonExportService _jsonExportService;
    private readonly ILogger<SimpleHttpServerService> _logger;
    private readonly IServiceProvider _services;
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly SemaphoreSlim _concurrentRequests;
    private readonly int _maxConcurrentRequests;
    private bool _disposed = false;

    public SimpleHttpServerService(
        JsonExportService jsonExportService,
        IConfiguration configuration,
        ILogger<SimpleHttpServerService> logger,
        IServiceProvider services)
    {
        _jsonExportService = jsonExportService;
        _logger = logger;
        _services = services;

        _port = configuration.GetValue<int>("JsonExport:HttpPort", 5005);
        _maxConcurrentRequests = configuration.GetValue<int>("MaxConcurrentHttpRequests", 20);
        _concurrentRequests = new SemaphoreSlim(_maxConcurrentRequests);

        _listener = new HttpListener();

        // Configurar prefijos para diferentes escenarios
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");

        // Solo agregar este si tienes los permisos necesarios
        // _listener.Prefixes.Add($"http://+:{_port}/");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Configurar límites de seguridad
            _listener.TimeoutManager.IdleConnection = TimeSpan.FromSeconds(30);
            _listener.TimeoutManager.EntityBody = TimeSpan.FromSeconds(30);

            _listener.Start();

            _logger.LogInformation("🌐 Servidor HTTP iniciado en puerto: {Port}", _port);
            _logger.LogInformation("📊 Límite de requests concurrentes: {Max}", _maxConcurrentRequests);


            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Esperar por una conexión
                    var context = await _listener.GetContextAsync().WaitAsync(stoppingToken);

                    // Procesar la solicitud en segundo plano con control de concurrencia
                    _ = ProcessRequestWithConcurrencyControl(context, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpListenerException hex) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Listener cerrado durante cancelación: {Message}", hex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error aceptando conexión HTTP");
                    await Task.Delay(1000, stoppingToken); // Esperar antes de reintentar
                }
            }
        }
        finally
        {
            _logger.LogInformation("🛑 Deteniendo servidor HTTP...");
            await StopHttpListenerAsync();
        }
    }

    private async Task ProcessRequestWithConcurrencyControl(HttpListenerContext context, CancellationToken ct)
    {
        // Esperar por un slot disponible
        await _concurrentRequests.WaitAsync(ct);

        try
        {
            await ProcessRequest(context, ct);
        }
        finally
        {
            _concurrentRequests.Release();
        }
    }

    private async Task ProcessRequest(HttpListenerContext context, CancellationToken ct)
    {
        var req = context.Request;
        var res = context.Response;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("📨 Request: {Method} {Url}", req.HttpMethod, req.Url?.AbsolutePath);

            // Configurar headers de seguridad básicos
            res.AddHeader("X-Content-Type-Options", "nosniff");
            res.AddHeader("X-Frame-Options", "DENY");

            if (req.HttpMethod != "GET")
            {
                await WriteErrorResponse(res, 405, "Método no permitido");
                return;
            }

            await HandleGetRequest(req, res, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Error procesando request {Url}", req.Url?.AbsolutePath);
            await WriteErrorResponse(res, 500, "Error interno del servidor");
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogDebug("✅ Response {StatusCode} en {Ms}ms: {Url}",
                res.StatusCode, stopwatch.ElapsedMilliseconds, req.Url?.AbsolutePath);
        }
    }

    private async Task HandleGetRequest(HttpListenerRequest request, HttpListenerResponse response, CancellationToken ct)
    {
        var path = (request.Url?.AbsolutePath ?? "").TrimEnd('/');

        switch (path)
        {
            case "/api/eventjson":
                await HandleGetAllFiles(response);
                break;

            case var p when p.StartsWith("/api/eventjson/") && !p.Contains("/descargar/"):
                await HandleGetSpecificFile(p.Replace("/api/eventjson/", ""), response, ct);
                break;

            case var p when p.StartsWith("/api/eventjson/descargar/"):
                await HandleDownloadFile(p.Replace("/api/eventjson/descargar/", ""), response, ct);
                break;

            case "/api/eventjson/estado":
                await HandleGetStatus(response);
                break;

            case "/health":
                await HandleHealthCheck(response);
                break;

            case "/metrics":
                await HandleMetrics(response, ct);
                break;

            case "/":
            case "/api":
                await WriteRedirect(response, "/api/eventjson");
                break;

            default:
                await WriteErrorResponse(response, 404, "Endpoint no encontrado");
                break;
        }
    }

    private static async Task WriteRedirect(HttpListenerResponse response, string redirectUrl)
    {
        response.StatusCode = 302;
        response.RedirectLocation = redirectUrl;
        await Task.CompletedTask;
        response.Close();
    }

    private async Task HandleHealthCheck(HttpListenerResponse response)
    {
        using var scope = _services.CreateScope();
        var healthCheckService = scope.ServiceProvider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        var healthResponse = new HealthCheckResponse(
            report.Status.ToString(),
            report.Entries.ToDictionary(
                e => e.Key,
                e => e.Value.Status.ToString()),
            DateTime.UtcNow
        );

        await WriteJsonResponse(response,
            report.Status == HealthStatus.Healthy ? 200 : 503,
            healthResponse);
    }

    private static async Task HandleMetrics(HttpListenerResponse response, CancellationToken ct)
    {
        response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
        response.StatusCode = 200;

        await using var outputStream = response.OutputStream;
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(outputStream, ct);

        response.Close();
    }

    private async Task HandleGetAllFiles(HttpListenerResponse response)
    {
        var archivos = _jsonExportService.ObtenerArchivosJsonDisponibles();
        var dto = new FileListResponse(archivos.Count, archivos, DateTime.UtcNow);
        await WriteJsonResponse(response, 200, dto);
    }

    private async Task HandleGetSpecificFile(string nombreArchivo, HttpListenerResponse response, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nombreArchivo) ||
            !nombreArchivo.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteErrorResponse(response, 400, "Nombre de archivo inválido");
            return;
        }

        var contenido = await _jsonExportService.LeerContenidoJsonAsync(nombreArchivo);
        if (contenido == null)
        {
            await WriteErrorResponse(response, 404, "Archivo no encontrado");
            return;
        }

        response.ContentType = "application/json; charset=utf-8";
        response.StatusCode = 200;

        var buffer = Encoding.UTF8.GetBytes(contenido);
        await response.OutputStream.WriteAsync(buffer, ct);
        response.Close();
    }

    private async Task HandleDownloadFile(string nombreArchivo, HttpListenerResponse response, CancellationToken ct)
    {
        var path = _jsonExportService.ObtenerRutaCompletaArchivo(nombreArchivo);
        if (path == null || !File.Exists(path))
        {
            await WriteErrorResponse(response, 404, "Archivo no encontrado");
            return;
        }

        var fileInfo = new FileInfo(path);
        var data = await File.ReadAllBytesAsync(path, ct);

        response.ContentType = "application/json; charset=utf-8";
        response.AddHeader("Content-Disposition", $"attachment; filename=\"{nombreArchivo}\"");
        response.AddHeader("Content-Length", fileInfo.Length.ToString());
        response.AddHeader("Last-Modified", fileInfo.LastWriteTimeUtc.ToString("R"));
        response.StatusCode = 200;

        await response.OutputStream.WriteAsync(data, ct);
        response.Close();
    }

    private async Task HandleGetStatus(HttpListenerResponse response)
    {
        var archivos = _jsonExportService.ObtenerArchivosJsonDisponibles();
        var dto = new StatusResponse(
            "Operativo",
            archivos.Count,
            archivos.Take(5),
            DateTime.UtcNow
        );
        await WriteJsonResponse(response, 200, dto);
    }

    private static async Task WriteJsonResponse<T>(HttpListenerResponse response, int statusCode, T data)
    {
        response.ContentType = "application/json; charset=utf-8";
        response.StatusCode = statusCode;

        string json;

        // Usar Source Generator cuando sea posible
        switch (data)
        {
            case FileListResponse fileList:
                json = JsonSerializer.Serialize(fileList, HttpApiJsonContext.Default.FileListResponse);
                break;
            case StatusResponse status:
                json = JsonSerializer.Serialize(status, HttpApiJsonContext.Default.StatusResponse);
                break;
            case ErrorResponse error:
                json = JsonSerializer.Serialize(error, HttpApiJsonContext.Default.ErrorResponse);
                break;
            case HealthCheckResponse health:
                json = JsonSerializer.Serialize(health, HttpApiJsonContext.Default.HealthCheckResponse);
                break;
            default:
                json = JsonSerializer.Serialize(data);
                break;
        }

        var buffer = Encoding.UTF8.GetBytes(json);
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    private static async Task WriteErrorResponse(HttpListenerResponse response, int statusCode, string message)
    {
        var dto = new ErrorResponse(message);
        await WriteJsonResponse(response, statusCode, dto);
    }

    

    private async Task StopHttpListenerAsync()
    {
        if (_listener.IsListening)
        {
            try
            {
                _listener.Stop();
                await Task.Delay(500); // Dar tiempo a que las conexiones se cierren
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al detener HttpListener");
            }
        }

        _listener.Close();
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            StopHttpListenerAsync().Wait(5000);
            _concurrentRequests?.Dispose();
            _listener?.Close();
            GC.SuppressFinalize(this);
            base.Dispose();
        }
    }
}
