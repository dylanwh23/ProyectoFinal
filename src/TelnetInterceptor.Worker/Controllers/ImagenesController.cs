using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using Microsoft.AspNetCore.Http;
using TelnetInterceptor.Worker.Services;
using TelnetInterceptor.Worker.Models; // Necesario para HistorySnapshot

namespace TelnetInterceptor.Worker.Controllers
{
    [ApiController]
    [Route("api")]
    public class ImagenesController : ControllerBase
    {
        private readonly CameraManagerService _cameraManager;
        private readonly ILogger<ImagenesController> _logger;

        public ImagenesController(CameraManagerService cameraManager, ILogger<ImagenesController> logger)
        {
            _cameraManager = cameraManager;
            _logger = logger;
        }

        // 1. Endpoint para Streaming MJPEG
        // GET: api/stream/{cameraName}
        [HttpGet("stream/{cameraName}")]
        public async Task Stream(string cameraName, CancellationToken ct)
        {
            Response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
            
            while (!ct.IsCancellationRequested)
            {
                // CAMBIO: Usamos GetLatestFile del nuevo servicio
                string? latestFile = _cameraManager.GetLatestFile(cameraName);
                
                if (latestFile != null && System.IO.File.Exists(latestFile))
                {
                    // null historyId porque es stream en vivo
                    byte[]? jpegData = await ProcessImageAsync(latestFile); 
                    
                    if (jpegData != null)
                    {
                        await Response.WriteAsync("--frame\r\n", ct);
                        await Response.WriteAsync("Content-Type: image/jpeg\r\n", ct);
                        await Response.WriteAsync($"Content-Length: {jpegData.Length}\r\n\r\n", ct);
                        await Response.Body.WriteAsync(jpegData, ct);
                        await Response.WriteAsync("\r\n", ct);
                        await Response.Body.FlushAsync(ct);
                    }
                }
                await Task.Delay(50, ct);
            }
        }

        // 2. Endpoint para obtener un Frame individual
        // GET: api/frame/{cameraName}?file=...
        [HttpGet("frame/{cameraName}")]
        public async Task<IActionResult> GetFrame(string cameraName, [FromQuery] string? file)
        {
            string fileToProcess;

            // LÓGICA ACTUALIZADA:
            // En la nueva arquitectura, FreezeHistory devuelve rutas absolutas en 'file'.
            // Por lo tanto, si nos pasan 'file', confiamos en que es la ruta correcta.
            if (!string.IsNullOrEmpty(file))
            {
                if (!System.IO.File.Exists(file))
                {
                    return NotFound("El archivo solicitado no existe.");
                }
                fileToProcess = file;
            }
            else
            {
                // Si no especifican archivo, devolvemos el último frame en vivo
                fileToProcess = _cameraManager.GetLatestFile(cameraName) ?? string.Empty;
                
                if (string.IsNullOrEmpty(fileToProcess) || !System.IO.File.Exists(fileToProcess))
                {
                    return NotFound("No se ha detectado ninguna imagen en vivo.");
                }
            }

            byte[]? jpegData = await ProcessImageAsync(fileToProcess);
            
            if (jpegData == null)
            {
                return StatusCode(500, "Error al procesar la imagen.");
            }

            return File(jpegData, "image/jpeg");
        }

        // 3. Endpoint para congelar historial (Snapshot rápido)
        // GET: api/history/freeze/{cameraName}
        [HttpGet("history/freeze/{cameraName}")]
        public IActionResult FreezeHistory(string cameraName)
        {
            try
            {
                // ADAPTACIÓN: El nuevo servicio requiere un rango. 
                // Simulamos un "Snapshot actual" pidiendo los últimos 10 segundos.
                var end = DateTime.Now;
                var start = end.AddSeconds(-10);

                var result = _cameraManager.FreezeHistory(cameraName, start, end);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem($"Error al congelar el historial: {ex.Message}");
            }
        }

        // 4. Endpoint para congelar historial por rango de tiempo explícito
        // GET: api/history/freeze-by-range-local/{cameraName}?startTime=...&endTime=...
        [HttpGet("history/freeze-by-range-local/{cameraName}")]
        public IActionResult FreezeHistoryByRange(
            string cameraName, 
            [FromQuery] DateTime startTime, 
            [FromQuery] DateTime endTime)
        {
            try
            {
                _logger.LogInformation("Solicitud rango: {start} a {end}", startTime, endTime);

                if (endTime <= startTime)
                    return BadRequest("La fecha de fin debe ser posterior a la fecha de inicio.");

                var snapshot = _cameraManager.FreezeHistory(cameraName, startTime, endTime);

                if (snapshot.Files.Count == 0)
                    return NotFound("No se encontraron imágenes para ese rango.");

                return Ok(snapshot);
            }
            catch (Exception ex)
            {
                return Problem($"Error al congelar por rango: {ex.Message}");
            }
        }

        // NOTA: El endpoint de "CleanupHistory" ([HttpDelete]) se eliminó porque 
        // la nueva arquitectura de CameraManagerService no crea carpetas temporales 
        // que necesiten limpieza manual (filtra en tiempo real).

        // --- Método Auxiliar Privado ---
        private async Task<byte[]?> ProcessImageAsync(string filePath)
        {
            // Reintento simple por si el archivo está bloqueado (escritura concurrente)
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    using var image = await Image.LoadAsync(filePath);
                    image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(1280, 720) }));
                    
                    using var ms = new MemoryStream();
                    await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85 });
                    return ms.ToArray();
                }
                catch (IOException)
                {
                    await Task.Delay(50); // Breve espera
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error procesando imagen {filePath}: {ex.Message}");
                    return null;
                }
            }
            return null;
        }
    }
}