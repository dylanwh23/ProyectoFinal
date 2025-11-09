namespace TelnetInterceptor.Worker.Services;

public interface IServicioFiltradoEventos
{
    // Retorna true si el evento es el primero de su IP después del Cooldown.
    bool DebeProcesarEvento(string ipCamara);
}