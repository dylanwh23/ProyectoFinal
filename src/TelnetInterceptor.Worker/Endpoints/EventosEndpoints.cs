using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TelnetInterceptor.Worker.Services;

namespace TelnetInterceptor.Worker.Endpoints;

public static class EventosEndpoints
{
    public static IEndpointRouteBuilder MapEventosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/eventos").WithTags("Eventos Permanentes");

        group.MapPost("/crear", async (CrearEventoRequest req, CameraManagerService manager) =>
        {
            var evento = await manager.CrearEventoPermanente(req.CameraName, req.Desde, req.Hasta, req.Nombre);
            return evento != null 
                ? Results.Ok(evento) 
                : Results.NotFound("No se encontraron imágenes o la cámara no existe");
        });

        group.MapGet("/", (CameraManagerService manager) =>
        {
            return Results.Ok(manager.ListarEventosGuardados());
        });

        // Endpoint para servir imágenes de eventos guardados
        group.MapGet("/{eventoId}/image/{imageName}", (string eventoId, string imageName, CameraManagerService manager) =>
        {
            // Reconstruimos la ruta basándonos en la configuración interna del manager
            // (Asumiendo que el manager expone o sabemos la ruta base _eventosOutputPath)
            // Para hacerlo limpio, idealmente el Manager debería tener un método GetEventImagePath(id, name)
            
            var basePath = @"C:\TelnetInterceptor_Data\EventosGenerados"; // Misma ruta que en el servicio
            var path = Path.Combine(basePath, eventoId, imageName);

            if (!System.IO.File.Exists(path)) return Results.NotFound();
            return Results.File(path, "image/jpeg");
        });

        return group;
    }
}

public record CrearEventoRequest(string CameraName, int Desde, int Hasta, string Nombre);