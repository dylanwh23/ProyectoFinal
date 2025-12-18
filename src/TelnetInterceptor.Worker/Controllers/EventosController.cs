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

        // GET: api/eventos/grid
        [HttpGet("grid")]
        public async Task<IActionResult> GetGridEvents()
        {
            try
            {
                var eventos = await _eventStorageService.GetEventsAsync();
                var grid = eventos
                    .Where(e => string.Equals(e.TipoEvento, "grid", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.FechaEvento ?? DateTime.MinValue)
                    .ToList();
                return Ok(grid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo eventos grid");
                return StatusCode(500, "Error obteniendo eventos grid");
            }
        }

        // GET: api/eventos/grid/{cameraIp}/{puerto}
        [HttpGet("grid/{cameraIp}/{puerto:int}")]
        public async Task<IActionResult> GetGridEventsByCamera(string cameraIp, int puerto)
        {
            try
            {
                var eventos = await _eventStorageService.GetEventsByCameraIpAsync(cameraIp);
                var grid = eventos
                    .Where(e => e.Puerto == puerto)
                    .Where(e => string.Equals(e.TipoEvento, "grid", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.FechaEvento ?? DateTime.MinValue)
                    .ToList();
                return Ok(grid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo eventos grid por cámara {CameraIp}:{Puerto}", cameraIp, puerto);
                return StatusCode(500, "Error obteniendo eventos grid por cámara");
            }
        }

        // GET: api/eventos/pallet
        [HttpGet("pallet")]
        public async Task<IActionResult> GetPalletEvents()
        {
            try
            {
                var eventos = await _eventStorageService.GetPalletEventsAsync();
                return Ok(eventos.OrderByDescending(e => e.FechaEvento).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo eventos pallet");
                return StatusCode(500, "Error obteniendo eventos pallet");
            }
        }

        // GET: api/eventos/pallet/{cameraIp}/{puerto}
        [HttpGet("pallet/{cameraIp}/{puerto:int}")]
        public async Task<IActionResult> GetPalletEventsByCamera(string cameraIp, int puerto)
        {
            try
            {
                var eventos = await _eventStorageService.GetPalletEventsByCameraIpAsync(cameraIp);
                return Ok(eventos.Where(e => e.Puerto == puerto).OrderByDescending(e => e.FechaEvento).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo eventos pallet por cámara {CameraIp}:{Puerto}", cameraIp, puerto);
                return StatusCode(500, "Error obteniendo eventos pallet por cámara");
            }
        }

        // GET: api/eventos/camion
        [HttpGet("camion")]
        public async Task<IActionResult> GetCamionEvents()
        {
            try
            {
                var eventos = await _eventStorageService.GetCamionEventsAsync();
                return Ok(eventos.OrderByDescending(e => e.FechaEvento).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo eventos camion");
                return StatusCode(500, "Error obteniendo eventos camion");
            }
        }

        // GET: api/eventos/camion/{cameraIp}/{puerto}
        [HttpGet("camion/{cameraIp}/{puerto:int}")]
        public async Task<IActionResult> GetCamionEventsByCamera(string cameraIp, int puerto)
        {
            try
            {
                var eventos = await _eventStorageService.GetCamionEventsByCameraIpAsync(cameraIp);
                return Ok(eventos.Where(e => e.Puerto == puerto).OrderByDescending(e => e.FechaEvento).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo eventos camion por cámara {CameraIp}:{Puerto}", cameraIp, puerto);
                return StatusCode(500, "Error obteniendo eventos camion por cámara");
            }
        }

        // GET: api/eventos/camion/estado
        [HttpGet("camion/estado")]
        public async Task<IActionResult> GetCamionEstado()
        {
            try
            {
                var eventos = await _eventStorageService.GetCamionEventsAsync();
                var estado = eventos
                    .GroupBy(e => $"{e.IpCamara}|{e.Puerto}|{e.Seccion ?? string.Empty}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.FechaEvento).First())
                    .Select(e => new CamionSeccionEstadoDto
                    {
                        IpCamara = e.IpCamara,
                        Puerto = e.Puerto,
                        Seccion = e.Seccion ?? string.Empty,
                        CamionId = e.CamionId,
                        Ocupado = e.Ocupado,
                        FechaEvento = e.FechaEvento,
                        TipoEvento = e.TipoEvento
                    })
                    .OrderBy(x => x.IpCamara)
                    .ThenBy(x => x.Seccion)
                    .ToList();

                return Ok(estado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo estado de camiones");
                return StatusCode(500, "Error obteniendo estado de camiones");
            }
        }

        // GET: api/eventos/camion/estado/{cameraIp}/{puerto}
        [HttpGet("camion/estado/{cameraIp}/{puerto:int}")]
        public async Task<IActionResult> GetCamionEstadoByCamera(string cameraIp, int puerto)
        {
            try
            {
                var eventos = await _eventStorageService.GetCamionEventsByCameraIpAsync(cameraIp);
                var estado = eventos
                    .Where(e => e.Puerto == puerto)
                    .GroupBy(e => e.Seccion ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.FechaEvento).First())
                    .Select(e => new CamionSeccionEstadoDto
                    {
                        IpCamara = e.IpCamara,
                        Puerto = e.Puerto,
                        Seccion = e.Seccion ?? string.Empty,
                        CamionId = e.CamionId,
                        Ocupado = e.Ocupado,
                        FechaEvento = e.FechaEvento,
                        TipoEvento = e.TipoEvento
                    })
                    .OrderBy(x => x.Seccion)
                    .ToList();

                return Ok(estado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo estado de camiones por cámara {CameraIp}:{Puerto}", cameraIp, puerto);
                return StatusCode(500, "Error obteniendo estado de camiones por cámara");
            }
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

        // DELETE: api/eventos/borrar-todo
        [HttpDelete("borrar-todo")]
        public async Task<IActionResult> ClearAllEvents()
        {
            try
            {
                await _eventStorageService.ClearAllEventsAsync();
                _logger.LogInformation("Todos los eventos han sido borrados.");
                return Ok("Todos los eventos han sido borrados.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error borrando todos los eventos");
                return StatusCode(500, "Error interno al borrar todos los eventos");
            }
        }

        private sealed class CamionSeccionEstadoDto
        {
            public string IpCamara { get; set; } = string.Empty;
            public int Puerto { get; set; }
            public string Seccion { get; set; } = string.Empty;
            public string CamionId { get; set; } = string.Empty;
            public bool Ocupado { get; set; }
            public DateTime FechaEvento { get; set; }
            public string TipoEvento { get; set; } = string.Empty;
        }
    }
}
