using System.Collections.Concurrent;
using TelnetInterceptor.Worker.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System;

namespace TelnetInterceptor.Worker.Services
{
    public class ServicioFiltradoEventos : IServicioFiltradoEventos
    {
        private readonly ConfiguracionInterceptor _configuracion;
        private readonly ILogger<ServicioFiltradoEventos> _logger;
        private readonly ConcurrentDictionary<string, string> _ultimoMensaje = new();
        private readonly ConcurrentDictionary<string, DateTime> _ultimoProcesamiento = new();


        public ServicioFiltradoEventos(
            IOptions<ConfiguracionInterceptor> configuracion,
            ILogger<ServicioFiltradoEventos> logger)
        {
            _configuracion = configuracion.Value;
            _logger = logger;
        }

        /// <summary>
        /// Aplica un cooldown configurable basado en la IP de la cámara.
        /// </summary>
        public bool DebeProcesarEvento(string ipCamara, string mensaje)
        {
            var cooldownSegundos = _configuracion.SegundosCooldown > 0 ? _configuracion.SegundosCooldown : 0;
            var cooldown = TimeSpan.FromSeconds(cooldownSegundos);
            var ahora = DateTime.UtcNow;

            // Si el mensaje anterior existe y es igual al actual, aplicamos cooldown
            if (_ultimoMensaje.TryGetValue(ipCamara, out var mensajePrevio) && mensajePrevio == mensaje)
            {
                if (_ultimoProcesamiento.TryGetValue(ipCamara, out var ultimo))
                {
                    if (ahora - ultimo < cooldown)
                    {
                        _logger.LogInformation("🚫 Evento filtrado por cooldown (mensaje repetido): {IpCamara}", ipCamara);
                        return false;
                    }
                }
            }

            // Actualizamos último mensaje y tiempo de procesamiento
            _ultimoMensaje[ipCamara] = mensaje;
            _ultimoProcesamiento[ipCamara] = ahora;

            _logger.LogDebug("✅ Evento de {Ip} procesado. Cooldown de {s}s aplicado si es repetido.", ipCamara, cooldownSegundos);
            return true;
        }

    }
}
