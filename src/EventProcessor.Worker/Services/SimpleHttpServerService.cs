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
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class HttpApiJsonContext : JsonSerializerContext
{
}

// DTOs para las respuestas de la API
public record FileListResponse(int TotalArchivos, List<string> Archivos, DateTime Timestamp);
public record StatusResponse(string Estado, int TotalArchivosJson, IEnumerable<string> UltimosArchivos, DateTime Timestamp);
public record ErrorResponse(string Error);

public class SimpleHttpServerService : BackgroundService
{
    private readonly JsonExportService _jsonExportService;
    private readonly ILogger<SimpleHttpServerService> _logger;
    private readonly HttpListener _listener;
    private readonly int _port;

    public SimpleHttpServerService(JsonExportService jsonExportService, IConfiguration configuration, ILogger<SimpleHttpServerService> logger)
    {
        _jsonExportService = jsonExportService;
        _logger = logger;
        _listener = new HttpListener();
        _port = configuration.GetValue<int>("JsonExport:HttpPort", 5005);

        _listener.Prefixes.Add($"http://localhost:{_port}/");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _listener.Start();
            _logger.LogInformation("[] Servidor HTTP iniciado en: http://localhost:{Puerto}", _port);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequest(context, stoppingToken), stoppingToken);
                }
                catch (HttpListenerException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "!! Error en el servidor HTTP");
                }
            }
        }
        finally
        {
            _listener.Stop();
            _listener.Close();
            _logger.LogInformation("[] Servidor HTTP detenido");
        }
    }

    private async Task ProcessRequest(HttpListenerContext context, CancellationToken stoppingToken)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            _logger.LogInformation(">> Request: {Method} {Url}", request.HttpMethod, request.Url?.AbsolutePath);

            if (request.HttpMethod == "GET")
            {
                await HandleGetRequest(request, response, stoppingToken);
            }
            else
            {
                await WriteErrorResponse(response, 405, "Método no permitido");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error procesando request");
            await WriteErrorResponse(response, 500, "Error interno del servidor");
        }
    }

    private async Task HandleGetRequest(HttpListenerRequest request, HttpListenerResponse response, CancellationToken stoppingToken)
    {
        var path = request.Url?.AbsolutePath ?? "";

        switch (path)
        {
            case "/api/eventjson":
                await HandleGetAllFiles(response);
                break;

            case var p when p.StartsWith("/api/eventjson/") && !p.Contains("/descargar/"):
                await HandleGetSpecificFile(p.Replace("/api/eventjson/", ""), response, stoppingToken);
                break;

            case var p when p.StartsWith("/api/eventjson/descargar/"):
                await HandleDownloadFile(p.Replace("/api/eventjson/descargar/", ""), response, stoppingToken);
                break;

            case "/api/eventjson/estado":
                await HandleGetStatus(response);
                break;

            default:
                await WriteErrorResponse(response, 404, "Endpoint no encontrado");
                break;
        }
    }

    private async Task HandleGetAllFiles(HttpListenerResponse response)
    {
        var archivos = _jsonExportService.ObtenerArchivosJsonDisponibles();

        _logger.LogInformation("[] Listando {Cantidad} archivos JSON", archivos.Count);

        var responseDto = new FileListResponse(archivos.Count, archivos, DateTime.UtcNow);
        await WriteJsonResponse(response, 200, responseDto);
    }

    private async Task HandleGetSpecificFile(string nombreArchivo, HttpListenerResponse response, CancellationToken stoppingToken)
    {
        _logger.LogInformation("[] Solicitado archivo JSON: {Archivo}", nombreArchivo);

        if (!nombreArchivo.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("!! Formato de archivo no válido: {Archivo}", nombreArchivo);
            await WriteErrorResponse(response, 400, "El archivo debe ser un JSON");
            return;
        }

        var contenido = await _jsonExportService.LeerContenidoJsonAsync(nombreArchivo);

        if (contenido == null)
        {
            _logger.LogWarning("!! Archivo no encontrado: {Archivo}", nombreArchivo);
            await WriteErrorResponse(response, 404, "Archivo no encontrado");
            return;
        }

        _logger.LogInformation(">> Archivo entregado exitosamente: {Archivo}", nombreArchivo);

        response.ContentType = "application/json";
        response.StatusCode = 200;
        var buffer = Encoding.UTF8.GetBytes(contenido);
        await response.OutputStream.WriteAsync(buffer, stoppingToken);
        response.Close();
    }

    private async Task HandleDownloadFile(string nombreArchivo, HttpListenerResponse response, CancellationToken stoppingToken)
    {
        _logger.LogInformation(">> Descarga solicitada para archivo: {Archivo}", nombreArchivo);

        var rutaArchivo = _jsonExportService.ObtenerRutaCompletaArchivo(nombreArchivo);

        if (rutaArchivo == null || !File.Exists(rutaArchivo))
        {
            _logger.LogWarning("!! Archivo no encontrado para descarga: {Archivo}", nombreArchivo);
            await WriteErrorResponse(response, 404, "Archivo no encontrado");
            return;
        }

        var fileBytes = await File.ReadAllBytesAsync(rutaArchivo, stoppingToken);

        response.ContentType = "application/json";
        response.AddHeader("Content-Disposition", $"attachment; filename=\"{nombreArchivo}\"");
        response.ContentLength64 = fileBytes.Length;
        response.StatusCode = 200;

        await response.OutputStream.WriteAsync(fileBytes, stoppingToken);
        _logger.LogInformation(">> Archivo descargado exitosamente: {Archivo}", nombreArchivo);
        response.Close();
    }

    private async Task HandleGetStatus(HttpListenerResponse response)
    {
        var archivos = _jsonExportService.ObtenerArchivosJsonDisponibles();

        var responseDto = new StatusResponse(
            "Operativo",
            archivos.Count,
            archivos.Take(5),
            DateTime.UtcNow
        );

        await WriteJsonResponse(response, 200, responseDto);
    }

    private static async Task WriteJsonResponse<T>(HttpListenerResponse response, int statusCode, T data)
    {
        response.ContentType = "application/json";
        response.StatusCode = statusCode;

        var jsonTypeInfo = HttpApiJsonContext.Default.GetTypeInfo(typeof(T));

        if (jsonTypeInfo is JsonTypeInfo<T> typedJsonTypeInfo)
        {
            var json = JsonSerializer.Serialize(data, typedJsonTypeInfo);
            var buffer = Encoding.UTF8.GetBytes(json);
            await response.OutputStream.WriteAsync(buffer);
        }
        else
        {
            var json = JsonSerializer.Serialize(data);
            var buffer = Encoding.UTF8.GetBytes(json);
            await response.OutputStream.WriteAsync(buffer);
        }

        response.Close();
    }

    private static async Task WriteErrorResponse(HttpListenerResponse response, int statusCode, string errorMessage)
    {
        var errorResponse = new ErrorResponse(errorMessage);
        await WriteJsonResponse(response, statusCode, errorResponse);
    }

    public override void Dispose()
    {
        _listener?.Stop();
        _listener?.Close();
        GC.SuppressFinalize(this);
        base.Dispose();
    }
}
