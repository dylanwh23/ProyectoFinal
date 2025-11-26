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
        var group = app.MapGroup("/eventos")
            .WithTags("Eventos")
            .WithDescription("Endpoints para gestionar eventos guardados permanentemente");

        // Crear evento (copiar rango a carpeta permanente)
        group.MapPost("/crear", CrearEvento)
            .WithDescription("Guarda un rango de imágenes como evento permanente")
            .WithOpenApi();

        // Listar todos los eventos guardados
        group.MapGet("/", ListarEventos)
            .WithDescription("Obtiene la lista de eventos guardados")
            .WithOpenApi();

        // Obtener detalles de un evento específico
        group.MapGet("/{eventoId}", ObtenerEvento)
            .WithDescription("Obtiene los detalles de un evento específico")
            .WithOpenApi();

        // Obtener imagen de un evento
        group.MapGet("/{eventoId}/image/{index}", ObtenerImagenEvento)
            .WithDescription("Obtiene una imagen específica de un evento guardado")
            .WithOpenApi();

        // Eliminar evento
        group.MapDelete("/{eventoId}", EliminarEvento)
            .WithDescription("Elimina un evento guardado permanentemente")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> CrearEvento(
        [FromBody] CrearEventoRequest request,
        EventosService eventosService,
        ILogger<EventosService> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.CameraName))
                return Results.BadRequest(new { error = "El nombre de la cámara es requerido" });

            if (request.Desde < 0 || request.Hasta < 0)
                return Results.BadRequest(new { error = "Los números deben ser positivos" });

            if (request.Desde > request.Hasta)
                return Results.BadRequest(new { error = "El número 'desde' debe ser menor o igual que 'hasta'" });

            var evento = await eventosService.CrearEvento(
                request.CameraName,
                request.Desde,
                request.Hasta,
                request.Nombre,
                request.Descripcion
            );

            if (evento == null)
                return Results.BadRequest(new { error = "No se encontraron imágenes en el rango especificado" });

            return Results.Ok(new
            {
                mensaje = "Evento creado correctamente",
                evento = new
                {
                    evento.EventoId,
                    evento.Nombre,
                    evento.CameraName,
                    evento.Desde,
                    evento.Hasta,
                    evento.CantidadImagenes,
                    evento.FechaCreacion,
                    urlVisor = $"/visor-evento.html?id={evento.EventoId}"
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al crear evento");
            return Results.Problem(
                detail: ex.Message,
                title: "Error al crear el evento"
            );
        }
    }

    private static IResult ListarEventos(
        EventosService eventosService,
        [FromQuery] string? cameraName = null)
    {
        var eventos = eventosService.ListarEventos(cameraName);

        return Results.Ok(new
        {
            count = eventos.Count,
            eventos = eventos.Select(e => new
            {
                e.EventoId,
                e.Nombre,
                e.CameraName,
                e.Desde,
                e.Hasta,
                e.CantidadImagenes,
                e.FechaCreacion,
                e.Descripcion,
                urlVisor = $"/visor-evento.html?id={e.EventoId}"
            })
        });
    }

    private static IResult ObtenerEvento(
        string eventoId,
        EventosService eventosService)
    {
        var evento = eventosService.ObtenerEvento(eventoId);

        if (evento == null)
            return Results.NotFound(new { error = "Evento no encontrado" });

        return Results.Ok(new
        {
            evento.EventoId,
            evento.Nombre,
            evento.CameraName,
            evento.Desde,
            evento.Hasta,
            evento.CantidadImagenes,
            evento.FechaCreacion,
            evento.Descripcion,
            imagenes = evento.Imagenes,
            urlVisor = $"/visor-evento.html?id={evento.EventoId}"
        });
    }

    private static IResult ObtenerImagenEvento(
        string eventoId,
        int index,
        EventosService eventosService,
        ILogger<EventosService> logger)
    {
        try
        {
            var imageBytes = eventosService.ObtenerImagenEvento(eventoId, index);

            if (imageBytes == null)
                return Results.NotFound(new { error = "Imagen no encontrada" });

            return Results.File(imageBytes, "image/bmp");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al obtener imagen del evento {eventoId}, índice {index}", eventoId, index);
            return Results.Problem(
                detail: ex.Message,
                title: "Error al obtener la imagen"
            );
        }
    }

    private static IResult EliminarEvento(
        string eventoId,
        EventosService eventosService,
        ILogger<EventosService> logger)
    {
        try
        {
            var resultado = eventosService.EliminarEvento(eventoId);

            if (!resultado)
                return Results.NotFound(new { error = "Evento no encontrado" });

            return Results.Ok(new { mensaje = "Evento eliminado correctamente" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al eliminar evento {eventoId}", eventoId);
            return Results.Problem(
                detail: ex.Message,
                title: "Error al eliminar el evento"
            );
        }
    }
}

public record CrearEventoRequest(
    string CameraName,
    int Desde,
    int Hasta,
    string? Nombre = null,
    string? Descripcion = null
);