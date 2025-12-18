using Shared.Contracts.Models;
using EventProcessor.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventProcessor.Worker.Services;

public class EventProcessorService(
    IServiceProvider serviceProvider,
    VideoLinkService videoLinkService,
    JsonExportService jsonExportService,
    WebhookService webhookService,
    ILogger<EventProcessorService> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly VideoLinkService _videoLinkService = videoLinkService;
    private readonly JsonExportService _jsonExportService = jsonExportService;
    private readonly WebhookService _webhookService = webhookService;
    private readonly ILogger<EventProcessorService> _logger = logger;

    /// <summary>
    /// Procesa un evento crudo, lo enriquece y lo persiste en la base de datos.
    /// </summary>
    public async Task<bool> ProcessAndStoreEventAsync(EventoMovimientoDetectado rawEvent)
    {
        if (rawEvent is null)
        {
            _logger.LogWarning("Evento recibido es NULL. Se ignora.");
            return false;
        }

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EventDbContext>();

        try
        {
            _logger.LogInformation(
                ">> Procesando evento | IP: {IpCamara} | Momento: {Momento}",
                rawEvent.IpCamara,
                rawEvent.Momento);

            // ------------------------------------------------------------
            // 1. Validaciones mínimas de entrada
            // ------------------------------------------------------------
            if (string.IsNullOrWhiteSpace(rawEvent.IpCamara))
            {
                _logger.LogWarning("Evento recibido con IP inválida.");
                return false;
            }

            // ------------------------------------------------------------
            // 2. Enriquecimiento del evento
            // ------------------------------------------------------------
            var videoLink = _videoLinkService.GenerateVideoLink(
                rawEvent.IpCamara,
                rawEvent.Momento
            );

            var cameraCategory = CategorizeCamera(rawEvent.IpCamara);
            var warehouseZone = GetWarehouseZone(rawEvent.IpCamara);
            var qrCode = ExtractQrCodeFromMessage(rawEvent.MensajeCrudoEvento);

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
                Confidence = 0.90
            };

            // ------------------------------------------------------------
            // 3. Persistencia
            // ------------------------------------------------------------
            await context.Events.AddAsync(enrichedEvent);
            await context.SaveChangesAsync();

            _logger.LogInformation(
                ">> Evento persistido correctamente | ID: {Id} | IP: {IpCamara}",
                enrichedEvent.Id,
                enrichedEvent.IpCamara
            );

            // ------------------------------------------------------------
            // 4. Exportación externa (JSON para WMS)
            // ------------------------------------------------------------
            await _jsonExportService.ExportarEventoAJsonAsync(enrichedEvent);

            // ------------------------------------------------------------
            // 5. Envío de webhooks
            // ------------------------------------------------------------
            await _webhookService.SendWebhookAsync(enrichedEvent);

            return true;
        }
        catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx,
                "!! Error de base de datos al guardar evento desde IP {Ip}",
                rawEvent.IpCamara);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "!! Error procesando evento desde IP: {IpCamara}",
                rawEvent.IpCamara);

            return false;
        }
    }

    // =====================================================================
    //  CATEGORIZACIÓN DE CÁMARAS
    // =====================================================================
    private static string CategorizeCamera(string ipCamara)
    {
        if (string.IsNullOrWhiteSpace(ipCamara))
            return "Unknown";

        return ipCamara switch
        {
            var ip when ip.StartsWith("192.168.1.") => "Seguridad-Perimetral",
            var ip when ip.StartsWith("192.168.2.") => "Monitoreo-Inventario",
            var ip when ip.StartsWith("192.168.3.") => "Control-Acceso",
            _ => "General-Monitoring"
        };
    }

    // =====================================================================
    //  ZONAS DEL DEPÓSITO
    // =====================================================================
    private static string GetWarehouseZone(string ipCamara)
    {
        if (string.IsNullOrWhiteSpace(ipCamara))
            return "Zona-Desconocida";

        return ipCamara switch
        {
            var ip when ip.StartsWith("192.168.1.") => "Zona-Recepcion",
            var ip when ip.StartsWith("192.168.2.") => "Pasillo-A",
            var ip when ip.StartsWith("192.168.3.") => "Muelle-Carga",
            _ => "Zona-General"
        };
    }

    // =====================================================================
    //  EXTRACCIÓN DE QR CODE
    // =====================================================================
    private static string? ExtractQrCodeFromMessage(string mensajeCrudo)
    {
        if (string.IsNullOrWhiteSpace(mensajeCrudo))
            return null;

        var delimiters = new[] { ' ', ';', ',', '|' };
        var parts = mensajeCrudo.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

        foreach (var p in parts)
        {
            if (p.StartsWith("QR:") || p.StartsWith("QR="))
            {
                var clean = p.Replace("QR:", "", StringComparison.OrdinalIgnoreCase)
                            .Replace("QR=", "", StringComparison.OrdinalIgnoreCase)
                            .Trim();

                return string.IsNullOrWhiteSpace(clean) ? null : clean;
            }
        }

        return null;
    }
}
