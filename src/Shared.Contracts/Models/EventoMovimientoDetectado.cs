namespace Shared.Contracts.Models;

public class EventoMovimientoDetectado
{
    public required DateTime Momento { get; set; }
    public required string IpCamara { get; set; }
    public required string MensajeCrudoEvento { get; set; }
}
