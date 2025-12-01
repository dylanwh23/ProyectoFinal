using System.Net;
using System.Text;
using System.Text.Json;

namespace EventProcessor.Worker.Services;

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
                    _ = Task.Run(() => ProcessRequest(context, stoppingToken));
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
                await WriteResponse(response, 405, new { Error = "Método no permitido" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error procesando request");
            await WriteResponse(response, 500, new { Error = "Error interno del servidor" });
        }
    }

    private async Task HandleGetRequest(HttpListenerRequest request, HttpListenerResponse response, CancellationToken stoppingToken)
    {
        var path = request.Url?.AbsolutePath ?? "";

        switch (path)
        {
            case "/api/eventjson":
                await HandleGetAllFiles(response, stoppingToken);
                break;

            case var p when p.StartsWith("/api/eventjson/") && !p.Contains("/descargar/"):
                await HandleGetSpecificFile(p.Replace("/api/eventjson/", ""), response, stoppingToken);
                break;

            case var p when p.StartsWith("/api/eventjson/descargar/"):
                await HandleDownloadFile(p.Replace("/api/eventjson/descargar/", ""), response, stoppingToken);
                break;

            case "/api/eventjson/estado":
                await HandleGetStatus(response, stoppingToken);
                break;

            default:
                await WriteResponse(response, 404, new { Error = "Endpoint no encontrado" });
                break;
        }
    }

    private async Task HandleGetAllFiles(HttpListenerResponse response, CancellationToken stoppingToken)
    {
        var archivos = _jsonExportService.ObtenerArchivosJsonDisponibles();

        _logger.LogInformation("[] Listando {Cantidad} archivos JSON", archivos.Count);

        await WriteResponse(response, 200, new
        {
            TotalArchivos = archivos.Count,
            Archivos = archivos,
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task HandleGetSpecificFile(string nombreArchivo, HttpListenerResponse response, CancellationToken stoppingToken)
    {
        _logger.LogInformation("[] Solicitado archivo JSON: {Archivo}", nombreArchivo);

        if (!nombreArchivo.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("!! Formato de archivo no válido: {Archivo}", nombreArchivo);
            await WriteResponse(response, 400, new { Error = "El archivo debe ser un JSON" });
            return;
        }

        var contenido = await _jsonExportService.LeerContenidoJsonAsync(nombreArchivo);

        if (contenido == null)
        {
            _logger.LogWarning("!! Archivo no encontrado: {Archivo}", nombreArchivo);
            await WriteResponse(response, 404, new { Error = "Archivo no encontrado" });
            return;
        }

        _logger.LogInformation(">> Archivo entregado exitosamente: {Archivo}", nombreArchivo);

        response.ContentType = "application/json";
        response.StatusCode = 200;
        var buffer = Encoding.UTF8.GetBytes(contenido);
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, stoppingToken);
        response.Close();
    }

    private async Task HandleDownloadFile(string nombreArchivo, HttpListenerResponse response, CancellationToken stoppingToken)
    {
        _logger.LogInformation(">> Descarga solicitada para archivo: {Archivo}", nombreArchivo);

        var rutaArchivo = _jsonExportService.ObtenerRutaCompletaArchivo(nombreArchivo);

        if (rutaArchivo == null || !File.Exists(rutaArchivo))
        {
            _logger.LogWarning("!! Archivo no encontrado para descarga: {Archivo}", nombreArchivo);
            await WriteResponse(response, 404, new { Error = "Archivo no encontrado" });
            return;
        }

        var fileBytes = await File.ReadAllBytesAsync(rutaArchivo, stoppingToken);

        response.ContentType = "application/json";
        response.AddHeader("Content-Disposition", $"attachment; filename=\"{nombreArchivo}\"");
        response.ContentLength64 = fileBytes.Length;
        response.StatusCode = 200;

        await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length, stoppingToken);
        _logger.LogInformation(">> Archivo descargado exitosamente: {Archivo}", nombreArchivo);
        response.Close();
    }

    private async Task HandleGetStatus(HttpListenerResponse response, CancellationToken stoppingToken)
    {
        var archivos = _jsonExportService.ObtenerArchivosJsonDisponibles();

        await WriteResponse(response, 200, new
        {
            Estado = "Operativo",
            TotalArchivosJson = archivos.Count,
            UltimosArchivos = archivos.Take(5),
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task WriteResponse(HttpListenerResponse response, int statusCode, object data)
    {
        response.ContentType = "application/json";
        response.StatusCode = statusCode;

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var buffer = Encoding.UTF8.GetBytes(json);
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.Close();
    }

    public override void Dispose()
    {
        _listener?.Stop();
        _listener?.Close();
        base.Dispose();
    }
}
