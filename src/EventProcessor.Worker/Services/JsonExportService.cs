using Microsoft.Extensions.Options;
using Shared.Contracts.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventProcessor.Worker.Services;

// Contexto de serialización con Source Generator
[JsonSerializable(typeof(WmsEventDto))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    IgnoreReadOnlyProperties = false)]
internal partial class WmsEventJsonContext : JsonSerializerContext
{
}

// DTO para el evento WMS
public record WmsEventDto
{
    public required string Id { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string CameraIp { get; init; }
    public string? CameraCategory { get; init; }
    public string? WarehouseZone { get; init; }
    public string? EventType { get; init; }
    public double? Confidence { get; init; }
    public string? RawMessage { get; init; }
    public string? VideoLink { get; init; }
    public DateTime? ProcessedAt { get; init; }
    public string? QrCode { get; init; }
}

public class JsonExportService
{
    private readonly string _jsonFolderPath;
    private readonly ILogger<JsonExportService> _logger;

    // Métricas de rendimiento
    private long _totalFilesExported = 0;
    private long _totalErrors = 0;

    public JsonExportService(IConfiguration configuration, ILogger<JsonExportService> logger)
    {
        _logger = logger;
        _jsonFolderPath = configuration["JsonExport:FolderPath"] ?? "./EventJsonExports";

        // Crear carpeta si no existe
        Directory.CreateDirectory(_jsonFolderPath);
        _logger.LogInformation("[] Carpeta de exportación JSON configurada en: {Ruta}", _jsonFolderPath);
    }

    public async Task<bool> ExportarEventoAJsonAsync(EnrichedEvent evento)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Crear DTO para WMS
            var wmsEvent = new WmsEventDto
            {
                Id = evento.EventId,
                Timestamp = evento.MomentoOriginal,
                CameraIp = evento.IpCamara,
                CameraCategory = evento.CameraCategory,
                WarehouseZone = evento.WarehouseZone,
                EventType = evento.EventType,
                Confidence = evento.Confidence,
                RawMessage = evento.MensajeCrudoEvento,
                VideoLink = evento.VideoLink,
                ProcessedAt = evento.ProcesadoEn,
                QrCode = evento.QrCodeDetected
            };

            // Usar Source Generator (más eficiente)
            var json = JsonSerializer.Serialize(wmsEvent, WmsEventJsonContext.Default.WmsEventDto);

            // Nombre del archivo: evento_{timestamp}_{id}.json
            var nombreArchivo = $"evento_{evento.MomentoOriginal:yyyyMMdd_HHmmss}_{evento.EventId}.json";
            var rutaArchivo = Path.Combine(_jsonFolderPath, nombreArchivo);

            await File.WriteAllTextAsync(rutaArchivo, json);

            _totalFilesExported++;

            _logger.LogInformation(
                ">> JSON exportado exitosamente: {Archivo} ({Ms}ms) - Total: {Total}",
                nombreArchivo, stopwatch.ElapsedMilliseconds, _totalFilesExported);

            return true;
        }
        catch (Exception ex)
        {
            _totalErrors++;
            _logger.LogError(ex,
                "!! Error exportando evento a JSON - EventId: {EventId} (Errores: {TotalErrors})",
                evento.EventId, _totalErrors);
            return false;
        }
    }

    public List<string> ObtenerArchivosJsonDisponibles()
    {
        try
        {
            var archivos = Directory.GetFiles(_jsonFolderPath, "*.json")
                .Select(Path.GetFileName)
                .Where(archivo => archivo != null)
                .Select(archivo => archivo!)
                .OrderByDescending(archivo => archivo)
                .ToList();

            _logger.LogDebug("[] Se encontraron {Cantidad} archivos JSON", archivos.Count);
            return archivos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "!! Error obteniendo lista de archivos JSON");
            return [];
        }
    }

    public string? ObtenerRutaCompletaArchivo(string nombreArchivo)
    {
        try
        {
            var rutaCompleta = Path.Combine(_jsonFolderPath, nombreArchivo);
            return File.Exists(rutaCompleta) ? rutaCompleta : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "!! Error obteniendo ruta del archivo: {Archivo}", nombreArchivo);
            return null;
        }
    }

    public async Task<string?> LeerContenidoJsonAsync(string nombreArchivo)
    {
        try
        {
            var rutaArchivo = ObtenerRutaCompletaArchivo(nombreArchivo);
            if (rutaArchivo == null)
            {
                _logger.LogWarning("[] Archivo no encontrado: {Archivo}", nombreArchivo);
                return null;
            }

            var contenido = await File.ReadAllTextAsync(rutaArchivo);
            _logger.LogDebug("[] Contenido leído del archivo: {Archivo}", nombreArchivo);
            return contenido;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "!! Error leyendo archivo JSON: {Archivo}", nombreArchivo);
            return null;
        }
    }

    // Método para obtener estadísticas
    public JsonExportStats GetStats()
    {
        return new JsonExportStats
        {
            TotalFilesExported = _totalFilesExported,
            TotalErrors = _totalErrors,
            FolderPath = _jsonFolderPath,
            FileCount = ObtenerArchivosJsonDisponibles().Count,
            LastUpdated = DateTime.UtcNow
        };
    }
}

// Clase para estadísticas
public class JsonExportStats
{
    public long TotalFilesExported { get; set; }
    public long TotalErrors { get; set; }
    public string? FolderPath { get; set; }
    public int FileCount { get; set; }
    public DateTime LastUpdated { get; set; }

    public double SuccessRate => TotalFilesExported > 0
        ? (TotalFilesExported - TotalErrors) / (double)TotalFilesExported * 100
        : 100;
}
