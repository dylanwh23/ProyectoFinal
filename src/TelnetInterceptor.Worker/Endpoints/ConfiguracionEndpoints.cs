using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace TelnetInterceptor.Worker.Endpoints;

public static class ConfiguracionEndpoints
{
    public static IEndpointRouteBuilder MapConfiguracionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/configuracion")
            .WithTags("Configuración")
            .WithDescription("Endpoints para gestionar la configuración del sistema");

        group.MapGet("/ruta-imagenes", ObtenerRutaImagenes)
            .WithDescription("Obtiene la ruta actual de la carpeta de imágenes")
            .WithOpenApi();

        group.MapPut("/ruta-imagenes", ActualizarRutaImagenes)
            .WithDescription("Actualiza la ruta de la carpeta de imágenes")
            .WithOpenApi();

        group.MapPost("/ruta-imagenes/validar", ValidarRutaImagenes)
            .WithDescription("Valida si una ruta es accesible y tiene permisos de escritura")
            .WithOpenApi();

        return app;
    }

    private static IResult ObtenerRutaImagenes(CameraStreamService cameraService)
    {
        var rutaActual = cameraService.GetWatchPath();
        return Results.Ok(new
        {
            rutaActual,
            existe = Directory.Exists(rutaActual),
            tienePermisoEscritura = ValidarPermisoEscritura(rutaActual)
        });
    }

    private static async Task<IResult> ActualizarRutaImagenes(
        CameraStreamService cameraService,
        [FromBody] ActualizarRutaRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.NuevaRuta))
            {
                return Results.BadRequest(new { error = "La ruta no puede estar vacía" });
            }

            // Normalizar la ruta
            var rutaNormalizada = Path.GetFullPath(request.NuevaRuta);

            // Validar que la ruta sea válida
            if (!Path.IsPathRooted(rutaNormalizada))
            {
                return Results.BadRequest(new { error = "La ruta debe ser una ruta absoluta" });
            }

            // Crear el directorio si no existe
            if (!Directory.Exists(rutaNormalizada))
            {
                Directory.CreateDirectory(rutaNormalizada);
            }

            // Validar permisos de escritura
            if (!ValidarPermisoEscritura(rutaNormalizada))
            {
                return Results.BadRequest(new { error = "No se tienen permisos de escritura en la ruta especificada" });
            }

            // Actualizar la ruta en el servicio
            await cameraService.ActualizarRutaBase(rutaNormalizada);

            return Results.Ok(new
            {
                mensaje = "Ruta actualizada correctamente",
                rutaAnterior = cameraService.GetWatchPath(),
                rutaNueva = rutaNormalizada
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.BadRequest(new { error = "Acceso denegado a la ruta especificada" });
        }
        catch (IOException ex)
        {
            return Results.BadRequest(new { error = $"Error de I/O: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                title: "Error al actualizar la ruta de imágenes"
            );
        }
    }

    private static IResult ValidarRutaImagenes([FromBody] ValidarRutaRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Ruta))
            {
                return Results.BadRequest(new { error = "La ruta no puede estar vacía" });
            }

            var rutaNormalizada = Path.GetFullPath(request.Ruta);

            var resultado = new
            {
                ruta = rutaNormalizada,
                esRutaAbsoluta = Path.IsPathRooted(rutaNormalizada),
                existe = Directory.Exists(rutaNormalizada),
                tienePermisoLectura = ValidarPermisoLectura(rutaNormalizada),
                tienePermisoEscritura = ValidarPermisoEscritura(rutaNormalizada),
                espacioDisponible = ObtenerEspacioDisponible(rutaNormalizada)
            };

            return Results.Ok(resultado);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new
            {
                error = $"Ruta inválida: {ex.Message}",
                esValida = false
            });
        }
    }

    private static bool ValidarPermisoLectura(string ruta)
    {
        try
        {
            if (!Directory.Exists(ruta)) return false;
            Directory.GetFiles(ruta);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidarPermisoEscritura(string ruta)
    {
        try
        {
            if (!Directory.Exists(ruta)) return false;

            var archivoTest = Path.Combine(ruta, $".test_{Guid.NewGuid()}.tmp");
            File.WriteAllText(archivoTest, "test");
            File.Delete(archivoTest);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ObtenerEspacioDisponible(string ruta)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(ruta)!);
            var espacioGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            return $"{espacioGB:F2} GB";
        }
        catch
        {
            return "No disponible";
        }
    }
}

public record ActualizarRutaRequest(string NuevaRuta);
public record ValidarRutaRequest(string Ruta);