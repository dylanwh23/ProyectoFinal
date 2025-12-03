using Shared.Contracts.Models;
using EventProcessor.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventProcessor.Worker.Services;

public class EventProcessorService(
    IServiceProvider serviceProvider,
    VideoLinkService videoLinkService,
    JsonExportService jsonExportService,
    ILogger<EventProcessorService> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly VideoLinkService _videoLinkService = videoLinkService;
    private readonly JsonExportService _jsonExportService = jsonExportService;
    private readonly ILogger<EventProcessorService> _logger = logger;

    public async Task<bool> ProcessAndStoreEventAsync(EventoMovimientoDetectado rawEvent)
    {
        // Crear un nuevo scope para cada procesamiento
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EventDbContext>();

        try
        {
            _logger.LogInformation("[] Procesando evento desde IP: {Ip}", rawEvent.IpCamara);

            // 1. Generar enlace de video
            var videoLink = _videoLinkService.GenerateVideoLink(rawEvent.IpCamara, rawEvent.Momento);

            // 2. Categorizar camara (logica simple basada en IP)
            var cameraCategory = CategorizeCamera(rawEvent.IpCamara);
            var warehouseZone = GetWarehouseZone(rawEvent.IpCamara);

            // 3. Extraer QR code si esta en el mensaje (analisis basico)
            var qrCode = ExtractQrCodeFromMessage(rawEvent.MensajeCrudoEvento);

            // 4. Crear evento enriquecido
            var enrichedEvent = new EnrichedEvent
            {
                MomentoOriginal = rawEvent.Momento,
                IpCamara = rawEvent.IpCamara,
                MensajeCrudoEvento = rawEvent.MensajeCrudoEvento,
                VideoLink = videoLink,
                CameraCategory = cameraCategory,
                WarehouseZone = warehouseZone,
                QrCodeDetected = qrCode,
                EventType = "movement_detected",
                Confidence = 0.9
            };

            // 5. Persistir en base de datos
            context.Events.Add(enrichedEvent);
            await context.SaveChangesAsync();

            // 6. Exportar a JSON para el WMS
            await _jsonExportService.ExportarEventoAJsonAsync(enrichedEvent);

            _logger.LogInformation(">> Evento almacenado exitosamente - ID: {Id}, IP: {Ip}",
                enrichedEvent.Id, rawEvent.IpCamara);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "!! Error procesando evento desde IP: {Ip}", rawEvent.IpCamara);
            return false;
        }
    }

    private static string CategorizeCamera(string ipCamara)
    {
        return ipCamara switch
        {
            var ip when ip.StartsWith("192.168.1.") => "Seguridad-Perimetral",
            var ip when ip.StartsWith("192.168.2.") => "Monitoreo-Inventario",
            var ip when ip.StartsWith("192.168.3.") => "Control-Acceso",
            _ => "General-Monitoring"
        };
    }

    private static string GetWarehouseZone(string ipCamara)
    {
        return ipCamara switch
        {
            var ip when ip.StartsWith("192.168.1.") => "Zona-Recepcion",
            var ip when ip.StartsWith("192.168.2.") => "Pasillo-A",
            var ip when ip.StartsWith("192.168.3.") => "Muelle-Carga",
            _ => "Zona-General"
        };
    }

    private static string? ExtractQrCodeFromMessage(string mensajeCrudo)
    {
        if (mensajeCrudo.Contains("QR:") || mensajeCrudo.Contains("QR="))
        {
            var parts = mensajeCrudo.Split(' ');
            var qrPart = parts.FirstOrDefault(p => p.StartsWith("QR:") || p.StartsWith("QR="));
            return qrPart?.Split(':').LastOrDefault()?.Split('=').LastOrDefault();
        }
        return null;
    }
}
