namespace Shared.Contracts.Models
{
    public class AltaEventoModel
    {
        public int Id { get; set; } // Clave primaria
        public string Nombre { get; set; } = string.Empty;
        public string IpCamara { get; set; } = string.Empty;
        public int Puerto { get; set; }
        public string RutaCarpeta { get; set; } = string.Empty;

        // Nuevos campos para eventos guardados
        public bool EsEventoGuardado { get; set; } = false;
        public int? FrameInicio { get; set; }
        public int? FrameFin { get; set; }
        public DateTime? FechaEvento { get; set; }
        public string? Descripcion { get; set; }

        // Frame de inicio (Nulo si es una cámara en vivo)
        public int? FromFrame { get; set; }

        // Frame de fin (Nulo si es una cámara en vivo)
        public int? ToFrame { get; set; }

        public bool EstaConectada { get; set; }
        public string? FramePath { get; set; }
    }
}
