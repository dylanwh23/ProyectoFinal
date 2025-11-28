using Microsoft.AspNetCore.Mvc;
using TelnetInterceptor.Worker.Models;
using TelnetInterceptor.Worker.Services;

namespace TelnetInterceptor.Worker.Controllers;

[ApiController]
[Route("api/camaras")]
public class CamaraController : ControllerBase
{
    private readonly CameraManagerService _manager;
    private readonly TelnetWorkerService _telnetWorker;

    public CamaraController(
        CameraManagerService manager, 
        TelnetWorkerService telnetWorker)
    {
        _manager = manager;
        _telnetWorker = telnetWorker;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EstadisticasCamara>>> ObtenerCamaras()
    {
        var camaras = await _manager.ObtenerCamarasBd();
        return Ok(camaras);
    }

    [HttpGet("estado")]
    public async Task<ActionResult<IEnumerable<EstadisticasCamara>>> ObtenerEstadoCamaras()
    {
        var camarasEnBd = await _manager.ObtenerCamarasBd();

        var statsEnVivo = _telnetWorker.ObtenerEstadisticas();

        var resultado = camarasEnBd.Select(cam => 
        {
            if (statsEnVivo.TryGetValue(cam.IpCamara, out var vivo)) return vivo; 
            return new EstadisticasCamara(cam.IpCamara, cam.Puerto, cam.RutaCarpeta, cam.Nombre)
            {
                EstaConectada = false,
                UltimoMensaje = "Desconectada / Sin Tráfico"
            };
        });

        return Ok(resultado);
    }

    [HttpPost]
    public async Task<IActionResult> AgregarCamara([FromBody] CamaraRequest request)
    {
        var exito = await _manager.AgregarCamara(request.IpCamara, request.Puerto, request.RutaCarpeta, request.Nombre);
        
        if (exito)
        {
            return Ok(new { mensaje = "Cámara agregada" });
        }
        else
        {
            return BadRequest(new { error = "La cámara ya existe" });
        }
    }

    [HttpDelete("{ip}")]
    public async Task<IActionResult> EliminarCamara(string ip)
    {
        var exito = await _manager.EliminarCamara(ip);
        
        if (exito) 
        {
            _telnetWorker.DesconectarCamara(ip);
        }

        if (exito)
        {
            return Ok(new { mensaje = "Cámara eliminada" });
        }
        else
        {
            return NotFound(new { error = "No encontrada" });
        }
    }

    [HttpGet("health/{ip}")]
    public IActionResult ObtenerSaludCamara(string ip)
    {
        var lastTime = _manager.GetLastImageTime(ip);
        
        if (lastTime == null) 
            return Ok(new { secondsAgo = 9999 }); // Nunca ha enviado

        var diff = (DateTime.UtcNow - lastTime.Value).TotalSeconds;
        return Ok(new { secondsAgo = diff });
    }
}

// DTO para la petición de agregar cámara
public record CamaraRequest(string IpCamara, int Puerto, string RutaCarpeta, string Nombre);