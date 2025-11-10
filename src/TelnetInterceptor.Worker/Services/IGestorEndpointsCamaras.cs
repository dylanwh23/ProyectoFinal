using Shared.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks; // <--- NECESARIO para Task

namespace TelnetInterceptor.Worker.Services;

public interface IGestorEndpointsCamaras
{
    IEnumerable<string> ObtenerCamaras();
    
    Task<bool> AgregarCamara(string ipCamara, int puerto);
    Task<bool> EliminarCamara(string ipCamara);

    Task PublicarEvento(EventoMovimientoDetectado evento, CancellationToken cancellationToken);
}
