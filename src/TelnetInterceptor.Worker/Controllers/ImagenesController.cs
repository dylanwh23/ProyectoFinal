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

        // ============================================================
        // 4. NUEVO: Endpoint para obtener imágenes por RANGO de número
        // GET: api/range/{cameraName}?from=1100&to=1200
        // ============================================================
        [HttpGet("range/{cameraName}")]
        public IActionResult GetRangeList(string cameraName, [FromQuery] int from, [FromQuery] int to)
        {
            if (from <= 0 || to <= 0)
                return BadRequest("Debe especificar 'from' y 'to' con valores positivos.");

            if (from > to)
                return BadRequest("'from' debe ser menor o igual a 'to'.");

            var snapshot = _cameraManager.GetFramesByRange(cameraName, from, to);

            if (snapshot.Files.Count == 0)
                return NotFound($"No hay imágenes en el rango {from}-{to}.");

            return Ok(snapshot);
        }

        // ============================================================
        // 5. NUEVO: Obtener información de rango disponible
        // GET: api/range-info/{cameraName}
        // ============================================================
        [HttpGet("range-info/{cameraName}")]
        public IActionResult GetRangeInfo(string cameraName)
        {
            var info = _cameraManager.GetAvailableRange(cameraName);

            if (info == null)
                return NotFound("Cámara no encontrada o sin imágenes.");

            return Ok(info);
        }

        // ============================================================
        // 6. NUEVO: Stream MJPEG de un rango específico (Playback)
        // GET: api/playback/{cameraName}?from=1100&to=1200&fps=10
        // ============================================================
        [HttpGet("playback/{cameraName}")]
        public async Task PlaybackStream(
            string cameraName,
            [FromQuery] int from,
            [FromQuery] int to,
            [FromQuery] int fps = 10,
            CancellationToken ct = default)
        {
            if (from <= 0 || to <= 0 || from > to)
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("Parámetros inválidos");
                return;
            }

            var snapshot = _cameraManager.GetFramesByRange(cameraName, from, to);

            if (snapshot.Files.Count == 0)
            {
                Response.StatusCode = 404;
                await Response.WriteAsync("No hay imágenes en el rango especificado");
                return;
            }

            Response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
            int delayMs = 1000 / Math.Clamp(fps, 1, 30);

            foreach (var filePath in snapshot.Files)
            {
                if (ct.IsCancellationRequested) break;

                if (!System.IO.File.Exists(filePath)) continue;

                byte[]? jpegData = await ProcessImageToJpegAsync(filePath);

                if (jpegData != null)
                {
                    await Response.WriteAsync("--frame\r\n", ct);
                    await Response.WriteAsync("Content-Type: image/jpeg\r\n", ct);
                    await Response.WriteAsync($"Content-Length: {jpegData.Length}\r\n\r\n", ct);
                    await Response.Body.WriteAsync(jpegData, ct);
                    await Response.WriteAsync("\r\n", ct);
                    await Response.Body.FlushAsync(ct);
                }

                await Task.Delay(delayMs, ct);
            }
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
                catch (Exception ex)
                {
                    _logger.LogError($"Error imagen {filePath}: {ex.Message}");
                    return null;
                }
            }
            return null;
        }
    }
}