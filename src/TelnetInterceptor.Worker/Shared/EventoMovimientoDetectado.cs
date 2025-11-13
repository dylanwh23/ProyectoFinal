namespace Shared.Contracts;

public class EventoMovimientoDetectado
{
    public string IpCamara { get; set; } = string.Empty;
    public DateTime Momento { get; set; }
    public string MensajeCrudoEvento { get; set; } = string.Empty;
}