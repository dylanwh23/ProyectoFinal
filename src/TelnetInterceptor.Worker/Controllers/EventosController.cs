using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Models;

namespace TelnetInterceptor.Worker.Controllers
{
    [ApiController]
    [Route("api/eventos")]
    public class EventosController : ControllerBase
    {
        private readonly ILogger<EventosController> _logger;

        // 1. INICIALIZAMOS CON DATOS DE PRUEBA
        // Para que cuando abras la consola, veas algo inmediatamente.
        private static List<AltaEventoModel> _eventosGuardados = new()
        {
            new AltaEventoModel
            {
                Nombre = "Intruso Detectado",
                IpCamara = "Camara1", // Asegúrate de usar nombres/IPs que tengas en tu config o UI
                Puerto = 23,
                FromFrame = 500,
                ToFrame = 510
            },
            new AltaEventoModel
            {
                Nombre = "Movimiento Nocturno",
                IpCamara = "Camara4",
                Puerto = 23,
                FromFrame = 200,
                ToFrame = 210
            }
        };

        public EventosController(ILogger<EventosController> logger)
        {
            _logger = logger;
        }

        // GET: api/eventos/lista
        [HttpGet("lista")]
        public IActionResult GetEventos()
        {
            try
            {
                // Ordenamos por fecha (si existe) o por nombre
                return Ok(_eventosGuardados);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo lista de eventos");
                return StatusCode(500, "Error obteniendo eventos");
            }
        }

        // POST: api/eventos/guardar
        [HttpPost("guardar")]
public async Task<IActionResult> GuardarEvento([FromBody] AltaEventoModel evento)
{
    try
    {
        if (evento == null) return BadRequest("Evento inválido");
        if (string.IsNullOrWhiteSpace(evento.Nombre)) return BadRequest("Falta nombre");
        if (string.IsNullOrWhiteSpace(evento.IpCamara)) return BadRequest("Falta IP");

        // --- VALIDACIÓN INTELIGENTE ---
        
        // CASO A: Es un Evento (Clip de video) -> DEBE tener frames
        if (evento.EsEventoGuardado)
        {
             if (evento.FromFrame == null || evento.ToFrame == null)
                return BadRequest("Un evento grabado debe tener rango de frames.");
        }
        
        // CASO B: Es una Cámara nueva -> NO TIENE frames (y está bien)
        // El Frontend manda FromFrame=null, así que pasará esta validación sin problemas.

        // 1. Guardar en Base de Datos (o memoria)
        // (Aquí asumo que usas tu servicio o lista estática)
        
        // Si usas el servicio CameraManager para agregarlo a la BD real:
        // await _cameraManager.AgregarCamara(evento.IpCamara, evento.Puerto, evento.RutaCarpeta, evento.Nombre);
        
        // O si sigues usando la lista estática de prueba del controlador:
        // _eventosGuardados.Add(evento); 

        _logger.LogInformation("Guardado: {Nombre} ({Type})", 
            evento.Nombre, 
            evento.FromFrame == null ? "Cámara Nueva" : "Evento Grabado");

        return Ok(evento);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error guardando");
        return StatusCode(500, "Error interno");
    }
}

        // GET: api/eventos/buscar/{cameraIp}
        [HttpGet("buscar/{cameraIp}")]
        public IActionResult GetEventosPorCamara(string cameraIp)
        {
            var eventos = _eventosGuardados
                .Where(e => e.IpCamara.Equals(cameraIp, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return Ok(eventos);
        }
    }
}