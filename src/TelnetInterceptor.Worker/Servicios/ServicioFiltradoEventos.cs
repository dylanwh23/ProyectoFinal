using Microsoft.Extensions.Options;
using TelnetInterceptor.Worker.Configuration;

namespace TelnetInterceptor.Worker.Services;

public class ServicioFiltradoEventos : IServicioFiltradoEventos
{
    // Dictionary para almacenar el estado (la hora del último evento procesado por cada IP)
    private readonly Dictionary<string, DateTime> _ultimoEventoPorIp = new();
    private readonly TimeSpan _cooldown;
    private readonly ILogger<ServicioFiltradoEventos> _registrador;

    public ServicioFiltradoEventos(
        IOptions<ConfiguracionInterceptor> configuracion,
        ILogger<ServicioFiltradoEventos> registrador)
    {
        // Obtiene el valor del cooldown de appsettings.json
        _cooldown = TimeSpan.FromSeconds(configuracion.Value.SegundosCooldown);
        _registrador = registrador;
    }

    public bool DebeProcesarEvento(string ipCamara)
    {
        lock (_ultimoEventoPorIp)
        {
            DateTime ahora = DateTime.UtcNow;

            // 1. Si la IP es nueva, o el cooldown ha expirado:
            if (!_ultimoEventoPorIp.TryGetValue(ipCamara, out DateTime ultimoMomento) ||
                (ahora - ultimoMomento) >= _cooldown)
            {
                // Actualiza el último momento y permite el procesamiento
                _ultimoEventoPorIp[ipCamara] = ahora;
                return true;
            }

            // 2. Si el cooldown NO ha expirado:
            return false;
        }
    }
}