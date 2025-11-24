namespace TelnetInterceptor.Worker.Services
{
    public interface IServicioFiltradoEventos
    {
        bool DebeProcesarEvento(string ipCamara, string evento);
    }
}
