using Microsoft.Extensions.Options;
using Shared.Contracts.Models;
using System.Text.Json;

namespace EventProcessor.Worker.Services;

public class JsonExportService
{
    private readonly string _jsonFolderPath;
    private readonly ILogger<JsonExportService> _logger;

    public JsonExportService(IConfiguration configuration, ILogger<JsonExportService> logger)
    {
        _logger = logger;
        _jsonFolderPath = configuration["JsonExport:FolderPath"] ?? "./EventJsonExports";

        // Crear carpeta si no existe
        Directory.CreateDirectory(_jsonFolderPath);
        _logger.LogInformation("📁 Carpeta de exportación JSON configurada en: {Ruta}", _jsonFolderPath);
    }

    public async Task<bool> ExportarEventoAJsonAsync(EnrichedEvent evento)
    {
        try
        {
            // Configuración para el JSON
            var opcionesJson = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Crear objeto con estructura específica para el WMS
            var eventoParaWms = new
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

            var json = JsonSerializer.Serialize(eventoParaWms, opcionesJson);

            // Nombre del archivo: evento_{timestamp}_{id}.json
            var nombreArchivo = $"evento_{evento.MomentoOriginal:yyyyMMdd_HHmmss}_{evento.EventId}.json";
            var rutaArchivo = Path.Combine(_jsonFolderPath, nombreArchivo);

            await File.WriteAllTextAsync(rutaArchivo, json);

            _logger.LogInformation("✅ JSON exportado exitosamente: {Archivo}", nombreArchivo);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error exportando evento a JSON - EventId: {EventId}", evento.EventId);
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

            _logger.LogDebug("📊 Se encontraron {Cantidad} archivos JSON", archivos.Count);
            return archivos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error obteniendo lista de archivos JSON");
            return new List<string>();
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
            _logger.LogError(ex, "❌ Error obteniendo ruta del archivo: {Archivo}", nombreArchivo);
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
                _logger.LogWarning("⚠️ Archivo no encontrado: {Archivo}", nombreArchivo);
                return null;
            }

            var contenido = await File.ReadAllTextAsync(rutaArchivo);
            _logger.LogDebug("📖 Contenido leído del archivo: {Archivo}", nombreArchivo);
            return contenido;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error leyendo archivo JSON: {Archivo}", nombreArchivo);
            return null;
        }
    }
}
