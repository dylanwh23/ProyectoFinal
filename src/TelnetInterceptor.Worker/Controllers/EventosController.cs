using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Models;
using TelnetInterceptor.Worker.Services; // Importar el nuevo servicio
using TelnetInterceptor.Worker.Models; // Para EstadisticasCamara

namespace TelnetInterceptor.Worker.Controllers
{
    [ApiController]
    [Route("api/eventos")]
    public class EventosController : ControllerBase
    {
        private readonly ILogger<EventosController> _logger;
        private readonly IEventStorageService _eventStorageService; // Inyectar el servicio de almacenamiento
        private readonly CameraManagerService _cameraManagerService; // Inyectar CameraManagerService

        public EventosController(
            ILogger<EventosController> logger,
            IEventStorageService eventStorageService,
            CameraManagerService cameraManagerService)
        {
            _logger = logger;
            _eventStorageService = eventStorageService;
            _cameraManagerService = cameraManagerService;
        }

        // GET: api/eventos/lista
        [HttpGet("lista")]
        public async Task<IActionResult> GetEventos()
        {
            try
            {
                var eventos = await _eventStorageService.GetEventsAsync();
                return Ok(eventos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo lista de eventos");
                return StatusCode(500, "Error obteniendo eventos");
            }
        }

        // POST: api/eventos/guardar (ahora se usará para registrar eventos en tiempo real o guardar clips)
        [HttpPost("guardar")]
        public async Task<IActionResult> GuardarEvento([FromBody] AltaEventoModel evento)
        {
            try
            {
                if (evento == null) return BadRequest("Evento inválido");
                if (string.IsNullOrWhiteSpace(evento.Nombre)) return BadRequest("Falta nombre");
                if (string.IsNullOrWhiteSpace(evento.IpCamara)) return BadRequest("Falta IP");

                // Obtener el último frame de la cámara si no es un evento guardado con frames específicos
                if (!evento.EsEventoGuardado || string.IsNullOrWhiteSpace(evento.FramePath))
                {
                    evento.FramePath = _cameraManagerService.GetLatestFile(evento.IpCamara);
                }

                evento.FechaEvento = DateTime.UtcNow; // Establecer la fecha del evento

                await _eventStorageService.SaveEventAsync(evento);

                _logger.LogInformation("Evento guardado: {Nombre} de {IpCamara}. Frame: {FramePath}",
                    evento.Nombre, evento.IpCamara, evento.FramePath ?? "N/A");

                return Ok(evento);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error guardando evento");
                return StatusCode(500, "Error interno al guardar evento");
            }
        }

        // GET: api/eventos/buscar/{cameraIp}
        [HttpGet("buscar/{cameraIp}")]
        public async Task<IActionResult> GetEventosPorCamara(string cameraIp)
        {
            try
            {
                var eventos = await _eventStorageService.GetEventsByCameraIpAsync(cameraIp);
                return Ok(eventos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error buscando eventos por cámara {CameraIp}", cameraIp);
                return StatusCode(500, "Error obteniendo eventos por cámara");
            }
        }
    }
}
