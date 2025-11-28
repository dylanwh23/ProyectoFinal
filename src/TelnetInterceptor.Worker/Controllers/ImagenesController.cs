using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using Microsoft.AspNetCore.Http;
using TelnetInterceptor.Worker.Services;
using TelnetInterceptor.Worker.Models;

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

        // 1. Endpoint para la Lista del Buffer (Para el botón de Pausa)
        // GET: api/buffer/{cameraName}?count=600
        [HttpGet("buffer/{cameraName}")]
        public IActionResult GetBufferList(string cameraName, [FromQuery] int count)
        {
            int limit = count > 0 ? count : 500;
            // Usamos la lógica de "Recientes por Secuencia" del servicio
            var snapshot = _cameraManager.GetRecentFrames(cameraName, limit);
            
            if (snapshot.Files.Count == 0) 
                return NotFound("No hay imágenes en el buffer.");
                
            return Ok(snapshot);
        }

        // 2. Endpoint para Streaming MJPEG (Vivo)
        // GET: api/stream/{cameraName}
        [HttpGet("stream/{cameraName}")]
        public async Task Stream(string cameraName, CancellationToken ct)
        {
            Response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
            string ultimoArchivoProcesado = "";

            while (!ct.IsCancellationRequested)
            {
                string? latestFile = _cameraManager.GetLatestFile(cameraName);
                
                if (!string.IsNullOrEmpty(latestFile) && 
                    latestFile != ultimoArchivoProcesado && 
                    System.IO.File.Exists(latestFile))
                {
                    byte[]? jpegData = await ProcessImageToJpegAsync(latestFile);
                    
                    if (jpegData != null)
                    {
                        ultimoArchivoProcesado = latestFile; 
                        await Response.WriteAsync("--frame\r\n", ct);
                        await Response.WriteAsync("Content-Type: image/jpeg\r\n", ct);
                        await Response.WriteAsync($"Content-Length: {jpegData.Length}\r\n\r\n", ct);
                        await Response.Body.WriteAsync(jpegData, ct);
                        await Response.WriteAsync("\r\n", ct);
                        await Response.Body.FlushAsync(ct);
                    }
                }
                await Task.Delay(100, ct); 
            }
        }

        // 3. Endpoint para obtener un Frame individual
        // GET: api/frame/{cameraName}?file=...
        [HttpGet("frame/{cameraName}")]
        public async Task<IActionResult> GetFrame(string cameraName, [FromQuery] string? file)
        {
            string fileToProcess;

            if (!string.IsNullOrEmpty(file))
            {
                if (!System.IO.File.Exists(file)) return NotFound("Archivo no encontrado.");
                fileToProcess = file;
            }
            else
            {
                fileToProcess = _cameraManager.GetLatestFile(cameraName) ?? string.Empty;
                if (string.IsNullOrEmpty(fileToProcess) || !System.IO.File.Exists(fileToProcess))
                    return NotFound("Sin imagen en vivo.");
            }

            byte[]? jpegData = await ProcessImageToJpegAsync(fileToProcess);
            if (jpegData == null) return StatusCode(500, "Error procesando imagen.");

            return File(jpegData, "image/jpeg");
        }

        // --- Helper Privado ---
        private async Task<byte[]?> ProcessImageToJpegAsync(string filePath)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    using var image = await Image.LoadAsync(filePath);
                    image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(1280, 720) }));
                    using var ms = new MemoryStream();
                    await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 75 });
                    return ms.ToArray();
                }
                catch (IOException) { await Task.Delay(50); }
                catch (Exception ex) { 
                    _logger.LogError($"Error imagen {filePath}: {ex.Message}"); 
                    return null; 
                }
            }
            return null;
        }
    }
}