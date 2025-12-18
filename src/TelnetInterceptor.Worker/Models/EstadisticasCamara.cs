using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace TelnetInterceptor.Worker.Models;

[PrimaryKey(nameof(IpCamara), nameof(Puerto))]
public class EstadisticasCamara
{
    public string Nombre { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty; // Nueva propiedad para categorizar cámaras
    public string TipoEvento { get; set; } = "grid"; // grid | pallet | camion
    public string IpCamara { get; set; } = string.Empty;
    public int Puerto { get; set; }
    
    // --- NUEVO: Configuración de Almacenamiento por Cámara ---
    public string RutaCarpeta { get; set; } = string.Empty; // Ruta UNC o Local

    // Estadísticas en tiempo real
    public bool EstaConectada { get; set; }
    public int MensajesRecibidos { get; set; }
    public DateTime? HoraUltimoMensaje { get; set; }
    public string? UltimoMensaje { get; set; }
    
    public EstadisticasCamara() { }

    public EstadisticasCamara(string ipCamara, int puerto, string rutaCarpeta, string nombre, string sucursal = "", string tipoEvento = "grid")
    {
        IpCamara = ipCamara;
        Puerto = puerto;
        RutaCarpeta = rutaCarpeta;
        EstaConectada = false;
        MensajesRecibidos = 0;
        Nombre = nombre;
        Sucursal = sucursal;
        TipoEvento = string.IsNullOrWhiteSpace(tipoEvento) ? "grid" : tipoEvento.ToLowerInvariant();
    }
}