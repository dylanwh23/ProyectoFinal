namespace Shared.Contracts.Models
{
    public class PalletEventModel
    {
        public int Id { get; set; }
        public string IpCamara { get; set; } = string.Empty;
        public int Puerto { get; set; }
        public string RutaCarpeta { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Sucursal { get; set; } = string.Empty;
        public int PalletId { get; set; }
        public string Cajas { get; set; } = string.Empty; // CAJA1|CAJA2
        public DateTime FechaEvento { get; set; } = DateTime.UtcNow;
        public string? FramePath { get; set; }
        public int? FromFrame { get; set; }
        public int? ToFrame { get; set; }
    }
}
