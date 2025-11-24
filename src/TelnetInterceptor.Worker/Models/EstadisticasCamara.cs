namespace TelnetInterceptor.Worker.Models;

public class EstadisticasCamara
{
    public string IpCamara { get; }
    public int Puerto { get; }
    public bool EstaConectada { get; set; }
    public int MensajesRecibidos { get; set; }
    public DateTime? HoraUltimoMensaje { get; set; }
    public string? UltimoMensaje { get; set; }
    
    public EstadisticasCamara(string ipCamara, int puerto)
    {
        IpCamara = ipCamara;
        Puerto = puerto;
        EstaConectada = false;
        MensajesRecibidos = 0;
    }
}