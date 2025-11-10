namespace Shared.Contracts.Models;

public class EnrichedEvent
{
    public int Id { get; set; }
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public DateTime MomentoOriginal { get; set; }
    public string IpCamara { get; set; } = string.Empty;
    public string MensajeCrudoEvento { get; set; } = string.Empty;
    public string VideoLink { get; set; } = string.Empty;
    public string CameraCategory { get; set; } = string.Empty;
    public string WarehouseZone { get; set; } = string.Empty;
    public DateTime ProcesadoEn { get; set; } = DateTime.UtcNow;

    public string? QrCodeDetected { get; set; }
    public string? EventType { get; set; }
    public double Confidence { get; set; } = 1.0;
}
