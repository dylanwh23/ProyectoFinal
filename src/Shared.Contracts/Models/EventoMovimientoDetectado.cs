namespace Shared.Contracts.Models;

public class EventoMovimientoDetectado
{
    public required DateTime Momento { get; set; }
    public required string IpCamara { get; set; }
    public required string MensajeCrudoEvento { get; set; }

    // Campos opcionales para eventos de grilla/estantería/caja
    public string? Estanteria { get; set; }
    public string? CajaQr { get; set; }
    // Valores esperados: "grid.alta", "grid.baja", "grid.movimiento"
    public string? TipoGridEvent { get; set; }
}
