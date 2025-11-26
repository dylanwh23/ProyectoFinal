using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace TelnetInterceptor.Worker.Endpoints;

public static class HistoryEndpoints
{
    public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/history")
            .WithTags("Historial")
            .WithDescription("Endpoints para gestionar y visualizar historial de imágenes");

        // Obtener lista de archivos por rango de números (sin copiar)
        group.MapGet("/list-by-range/{cameraName}", ListarPorRangoNumeros)
            .WithDescription("Lista imágenes en un rango de números sin copiarlas")
            .WithOpenApi();

        // Congelar historial por rango (crea copia temporal)
        group.MapGet("/freeze-by-range/{cameraName}", CongelarHistorialPorRangoNumeros)
            .WithDescription("Congela un rango de imágenes por número de snapshot (crea copia temporal)")
            .WithOpenApi();

        // Obtener imagen específica por número
        group.MapGet("/image/{cameraName}/{number}", ObtenerImagenPorNumero)
            .WithDescription("Obtiene una imagen específica por su número de snapshot")
            .WithOpenApi();

        // Limpiar carpeta de historial
        group.MapDelete("/cleanup/{cameraName}/{historyId}", LimpiarHistorial)
            .WithDescription("Elimina una carpeta de historial temporal")
            .WithOpenApi();

        return app;
    }

    private static IResult ListarPorRangoNumeros(
        string cameraName,
        [FromQuery] int desde,
        [FromQuery] int hasta,
        CameraStreamService cameraService,
        ILogger<CameraStreamService> logger)
    {
        try
        {
            if (desde < 0 || hasta < 0)
            {
                return Results.BadRequest(new { error = "Los números de snapshot deben ser positivos" });
            }

            if (desde > hasta)
            {
                return Results.BadRequest(new { error = "El número 'desde' debe ser menor o igual que 'hasta'" });
            }

            string cameraPath = cameraService.GetCameraPath(cameraName);

            if (!Directory.Exists(cameraPath))
            {
                return Results.NotFound(new { error = $"No se encontró la carpeta de la cámara {cameraName}" });
            }

            var dirInfo = new DirectoryInfo(cameraPath);
            var allFiles = dirInfo.GetFiles("*.bmp", SearchOption.TopDirectoryOnly);
            var filesInRange = new List<(string fileName, int number)>();

            foreach (var file in allFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file.Name);
                var parts = fileName.Split('_');

                if (parts.Length >= 2 && int.TryParse(parts[^1], out int snapshotNumber))
                {
                    if (snapshotNumber >= desde && snapshotNumber <= hasta)
                    {
                        filesInRange.Add((file.Name, snapshotNumber));
                    }
                }
            }

            var sortedFiles = filesInRange.OrderBy(x => x.number)
                                          .Select(x => new {
                                              fileName = x.fileName,
                                              number = x.number
                                          })
                                          .ToList();

            if (sortedFiles.Count == 0)
            {
                return Results.Ok(new
                {
                    cameraName,
                    files = sortedFiles,
                    count = 0,
                    mensaje = $"No se encontraron imágenes entre {desde} y {hasta}",
                    rangoSolicitado = new { desde, hasta }
                });
            }

            return Results.Ok(new
            {
                cameraName,
                files = sortedFiles,
                count = sortedFiles.Count,
                rangoSolicitado = new { desde, hasta },
                rangoEncontrado = new
                {
                    primerNumero = sortedFiles.First().number,
                    ultimoNumero = sortedFiles.Last().number
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al listar historial por rango numérico para {camera}", cameraName);
            return Results.Problem(
                detail: ex.Message,
                title: "Error al listar el historial por rango"
            );
        }
    }

    private static IResult CongelarHistorialPorRangoNumeros(
        string cameraName,
        [FromQuery] int desde,
        [FromQuery] int hasta,
        CameraStreamService cameraService,
        ILogger<CameraStreamService> logger)
    {
        try
        {
            if (desde < 0 || hasta < 0)
            {
                return Results.BadRequest(new { error = "Los números de snapshot deben ser positivos" });
            }

            if (desde > hasta)
            {
                return Results.BadRequest(new { error = "El número 'desde' debe ser menor o igual que 'hasta'" });
            }

            var snapshot = cameraService.FreezeHistoryByNumberRange(cameraName, desde, hasta);

            if (snapshot.Files.Count == 0)
            {
                return Results.Ok(new
                {
                    historyId = snapshot.HistoryId,
                    files = snapshot.Files,
                    mensaje = $"No se encontraron imágenes entre {desde} y {hasta}",
                    rangoSolicitado = new { desde, hasta }
                });
            }

            return Results.Ok(new
            {
                historyId = snapshot.HistoryId,
                files = snapshot.Files,
                count = snapshot.Files.Count,
                rangoSolicitado = new { desde, hasta },
                advertencia = "Este historial se eliminará automáticamente en 5 minutos"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al congelar historial por rango numérico para {camera}", cameraName);
            return Results.Problem(
                detail: ex.Message,
                title: "Error al congelar el historial por rango"
            );
        }
    }

    private static IResult ObtenerImagenPorNumero(
        string cameraName,
        int number,
        CameraStreamService cameraService,
        ILogger<CameraStreamService> logger)
    {
        try
        {
            if (number < 0)
            {
                return Results.BadRequest(new { error = "El número de snapshot debe ser positivo" });
            }

            string cameraPath = cameraService.GetCameraPath(cameraName);

            if (!Directory.Exists(cameraPath))
            {
                return Results.NotFound(new { error = $"No se encontró la carpeta de la cámara {cameraName}" });
            }

            var dirInfo = new DirectoryInfo(cameraPath);
            var allFiles = dirInfo.GetFiles("*.bmp", SearchOption.TopDirectoryOnly);

            foreach (var file in allFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file.Name);
                var parts = fileName.Split('_');

                if (parts.Length >= 2 && int.TryParse(parts[^1], out int snapshotNumber))
                {
                    if (snapshotNumber == number)
                    {
                        var imageBytes = File.ReadAllBytes(file.FullName);
                        return Results.File(imageBytes, "image/bmp");
                    }
                }
            }

            return Results.NotFound(new { error = $"No se encontró la imagen con número {number}" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al obtener imagen {number} de {camera}", number, cameraName);
            return Results.Problem(
                detail: ex.Message,
                title: "Error al obtener la imagen"
            );
        }
    }

    private static IResult LimpiarHistorial(
        string cameraName,
        string historyId,
        CameraStreamService cameraService,
        ILogger<CameraStreamService> logger)
    {
        try
        {
            cameraService.CleanupHistoryFolder(cameraName, historyId);
            return Results.Ok(new { mensaje = $"Historial {historyId} limpiado correctamente" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al limpiar historial {historyId} de {camera}", historyId, cameraName);
            return Results.Problem(
                detail: ex.Message,
                title: "Error al limpiar el historial"
            );
        }
    }
}