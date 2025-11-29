using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelnetInterceptor.Worker.Models
{
    /// <summary>
    /// DTO que representa información sobre el rango de frames disponibles para una cámara
    /// </summary>
    public class RangeInfoDto
    {
        /// <summary>
        /// Número del frame más antiguo disponible
        /// </summary>
        public int MinNumber { get; set; }

        /// <summary>
        /// Número del frame más reciente disponible
        /// </summary>
        public int MaxNumber { get; set; }

        /// <summary>
        /// Total de frames disponibles en el buffer
        /// </summary>
        public int TotalFrames { get; set; }

        /// <summary>
        /// Nombre/ID del historial
        /// </summary>
        public string HistoryId { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO que representa un snapshot del buffer de frames
    /// </summary>
    public class HistorySnapshotDto
    {
        /// <summary>
        /// ID del historial/cámara
        /// </summary>
        public string HistoryId { get; set; } = string.Empty;

        /// <summary>
        /// Lista de rutas de archivos de frames
        /// </summary>
        public List<string> Files { get; set; } = new();
    }
}