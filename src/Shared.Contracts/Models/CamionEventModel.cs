using System.ComponentModel.DataAnnotations;

namespace Shared.Contracts.Models
{
    public class CamionEventModel
    {
        [Key]
        public int Id { get; set; }
        public string IpCamara { get; set; } = string.Empty;
        public int Puerto { get; set; }
        public string Seccion { get; set; } = string.Empty;
        public string CamionId { get; set; } = string.Empty;
        public string TipoEvento { get; set; } = string.Empty; // camion.llego | camion.sefue
        public bool Ocupado { get; set; }
        public DateTime FechaEvento { get; set; }
        public string? Raw { get; set; }
        public string? RutaCarpeta { get; set; }
        public string? FramePath { get; set; }
    }
}
