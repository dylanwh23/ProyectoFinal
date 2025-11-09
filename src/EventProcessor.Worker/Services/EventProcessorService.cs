using Shared.Contracts.Models;
using EventProcessor.Worker.Data;
using Microsoft.EntityFrameworkCore;

namespace EventProcessor.Worker.Services;

public class EventProcessorService
{
    private readonly EventDbContext _context;
    private readonly VideoLinkService _videoLinkService;
    private readonly ILogger<EventProcessorService> _logger;

    public EventProcessorService(
        EventDbContext context,
        VideoLinkService videoLinkService,
        ILogger<EventProcessorService> logger)
    {
        _context = context;
        _videoLinkService = videoLinkService;
        _logger = logger;
    }

    public async Task<bool> ProcessAndStoreEventAsync(EventoMovimientoDetectado rawEvent)
    {
        try
        {
            _logger.LogInformation("Processing event from IP: {Ip}", rawEvent.IpCamara);

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
            _context.Events.Add(enrichedEvent);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Event stored successfully - ID: {Id}, IP: {Ip}",
                enrichedEvent.Id, rawEvent.IpCamara);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event from IP: {Ip}", rawEvent.IpCamara);
            return false;
        }
    }

    private string CategorizeCamera(string ipCamara)
    {
        return ipCamara switch
        {
            var ip when ip.StartsWith("192.168.1.") => "Seguridad-Perimetral",
            var ip when ip.StartsWith("192.168.2.") => "Monitoreo-Inventario",
            var ip when ip.StartsWith("192.168.3.") => "Control-Acceso",
            _ => "General-Monitoring"
        };
    }

    private string GetWarehouseZone(string ipCamara)
    {
        return ipCamara switch
        {
            var ip when ip.StartsWith("192.168.1.") => "Zona-Recepcion",
            var ip when ip.StartsWith("192.168.2.") => "Pasillo-A",
            var ip when ip.StartsWith("192.168.3.") => "Muelle-Carga",
            _ => "Zona-General"
        };
    }

    private string? ExtractQrCodeFromMessage(string mensajeCrudo)
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
