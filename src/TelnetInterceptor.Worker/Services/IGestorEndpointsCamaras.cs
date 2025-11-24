using Shared.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks; // <--- NECESARIO para Task
using TelnetInterceptor.Worker.Configuration; // Added for ConfiguracionCamara

namespace TelnetInterceptor.Worker.Services;

public interface IGestorEndpointsCamaras
{
    IEnumerable<string> ObtenerCamaras();
    ConfiguracionCamara? ObtenerCamara(string ipCamara);
    
    Task<bool> AgregarCamara(string ipCamara, int puerto);
    Task<bool> EliminarCamara(string ipCamara);

    Task PublicarEvento(EventoMovimientoDetectado evento, CancellationToken cancellationToken);
}
