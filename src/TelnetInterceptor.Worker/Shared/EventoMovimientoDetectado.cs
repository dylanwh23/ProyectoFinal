using System.Text.Json.Serialization;

namespace Shared.Contracts;

public class EventoMovimientoDetectado
{
    [JsonPropertyName("ipCamara")]
    public string IpCamara { get; set; } = string.Empty;
    
    [JsonPropertyName("momento")]
    public DateTime Momento { get; set; }
    
    [JsonPropertyName("mensajeCrudoEvento")]
    public string MensajeCrudoEvento { get; set; } = string.Empty;
}