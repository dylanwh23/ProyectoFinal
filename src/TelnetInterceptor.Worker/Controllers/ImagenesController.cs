using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using Microsoft.AspNetCore.Http;

namespace TelnetInterceptor.Worker.Controllers
{
    [ApiController]
    [Route("api")] // Prefijo base, así las rutas quedan como /api/stream/..., /api/frame/...
    public class ImagenesController : ControllerBase
    {
        private readonly CameraStreamService _cameraService;
        private readonly ILogger<ImagenesController> _logger;

        // Inyección de dependencia a través del constructor (Estándar de Controllers)
        public ImagenesController(CameraStreamService cameraService, ILogger<ImagenesController> logger)
        {
            _cameraService = cameraService;
            _logger = logger;
        }

        // 1. Endpoint para Streaming MJPEG
        // GET: api/stream/{cameraName}
        [HttpGet("stream/{cameraName}")]
        public async Task Stream(string cameraName, CancellationToken ct)
        {
            Response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
            
            // Mantenemos el bucle infinito escribiendo en el Response.Body
            while (!ct.IsCancellationRequested)
            {
                string? latestFile = _cameraService.GetLatestFileForCamera(cameraName);
                if (latestFile != null && System.IO.File.Exists(latestFile))
                {
                    byte[]? jpegData = await ProcessImageAsync(latestFile, null);
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
        // GET: api/frame/{cameraName}?file=...&historyId=...
        [HttpGet("frame/{cameraName}")]
        public async Task<IActionResult> GetFrame(string cameraName, [FromQuery] string? file, [FromQuery] string? historyId)
        {
            string fileToProcess;

            if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(historyId))
            {
                string safeHistoryId = Path.GetFileName(historyId);
                string safeFileName = Path.GetFileName(file);
                fileToProcess = Path.Combine(_cameraService.GetCameraPath(cameraName), safeHistoryId, safeFileName);
                
                if (!System.IO.File.Exists(fileToProcess))
                {
                    return NotFound("El archivo de historial ya no existe.");
                }
            }
            else
            {
                fileToProcess = _cameraService.GetLatestFileForCamera(cameraName);
                if (fileToProcess == null || !System.IO.File.Exists(fileToProcess))
                {
                    return NotFound("No se ha detectado ninguna imagen en vivo.");
                }
            }

            byte[]? jpegData = await ProcessImageAsync(fileToProcess, historyId);
            
            if (jpegData == null)
            {
                return StatusCode(500, "Error al procesar la imagen.");
            }

            // En Controllers, devolver un archivo es más limpio usando File()
            return File(jpegData, "image/jpeg");
        }

        // 3. Endpoint para congelar historial (snapshot actual)
        // GET: api/history/freeze/{cameraName}
        [HttpGet("history/freeze/{cameraName}")]
        public IActionResult FreezeHistory(string cameraName)
        {
            try
            {
                var result = _cameraService.FreezeHistory(cameraName);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem($"Error al congelar el historial: {ex.Message}");
            }
        }

        // 4. Endpoint para congelar historial por rango de tiempo
        // GET: api/history/freeze-by-range-local/{cameraName}?startTime=...&endTime=...
        [HttpGet("history/freeze-by-range-local/{cameraName}")]
        public IActionResult FreezeHistoryByRange(
            string cameraName, 
            [FromQuery] DateTime startTime, 
            [FromQuery] DateTime endTime)
        {
            try
            {
                _logger.LogInformation("Recibida solicitud de rango local: {start} a {end}", startTime, endTime);

                if (endTime <= startTime)
                {
                    return BadRequest("La fecha de fin debe ser posterior a la fecha de inicio.");
                }

                var snapshot = _cameraService.FreezeHistoryByTimeRangeLocal(cameraName, startTime, endTime);

                if (snapshot.Files.Count == 0)
                {
                    return NotFound("No se encontraron imágenes para ese rango de hora local.");
                }

                return Ok(snapshot);
            }
            catch (Exception ex)
            {
                return Problem($"Error al congelar el historial por rango local: {ex.Message}");
            }
        }

        // 5. Endpoint para borrar un historial específico
        // DELETE: api/history/cleanup/{cameraName}/{historyId}
        [HttpDelete("history/cleanup/{cameraName}/{historyId}")]
        public IActionResult CleanupHistory(string cameraName, string historyId)
        {
            try
            {
                _cameraService.CleanupHistoryFolder(cameraName, historyId);
                return Ok(new { message = "Historial borrado" });
            }
            catch (Exception ex)
            {
                return Problem($"Error al borrar historial: {ex.Message}");
            }
        }

        // --- Método Auxiliar Privado (Lógica de ImageSharp) ---
        private async Task<byte[]?> ProcessImageAsync(string filePath, string? historyId)
        {
            for (int i = 0; i < 5; i++)
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
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error procesando {filePath}: {ex.Message}");
                    return null;
                }
            }
            return null;
        }
    }

    // --- Clases Auxiliares ---
    // Puedes mover estas a una carpeta 'Models' o 'DTOs' si prefieres más orden,
    // pero aquí funcionan perfectamente.

    public class ServerSettings 
    { 
        public string WatchPath { get; set; } = "C:\\Public"; 
    }

    public class HistorySnapshot 
    { 
        public string HistoryId { get; set; } = string.Empty; 
        public List<string> Files { get; set; } = new List<string>(); 
    }
}