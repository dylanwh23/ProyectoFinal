using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TelnetInterceptor.Worker.Models;
using TelnetInterceptor.Worker.Services;

namespace TelnetInterceptor.Worker.Endpoints;

public static class CamaraEndpoints
{
    public static void MapCamaraEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/camaras")
            .WithTags("Cámaras")
            .WithDescription("Endpoints para gestionar las cámaras");

        group.MapGet("/", ObtenerCamaras)
            .WithDescription("Obtiene la lista de cámaras registradas")
            .WithOpenApi();

        group.MapGet("/estado", ObtenerEstadoCamaras)
            .WithDescription("Obtiene el estado actual de todas las cámaras")
            .WithOpenApi();

        group.MapPost("/", AgregarCamara)
            .WithDescription("Registra una nueva cámara")
            .WithOpenApi();

        group.MapDelete("/{ip}", EliminarCamara)
            .WithDescription("Elimina una cámara registrada")
            .WithOpenApi();
    }

    private static IResult ObtenerCamaras(IGestorEndpointsCamaras gestor)
    {
        var camaras = gestor.ObtenerCamaras();
        return Results.Ok(camaras);
    }

    private static IResult ObtenerEstadoCamaras(Worker worker, IGestorEndpointsCamaras gestor)
{
    var todasLasIps = gestor.ObtenerCamaras();
    var estadisticas = worker.ObtenerEstadisticas().ToDictionary(e => e.IpCamara);

    var resultado = todasLasIps.Select(ip =>
    {
        if (estadisticas.TryGetValue(ip, out var stat))
            return stat;

        // Si no hay estadísticas aún, devolvemos un estado "Desconectada"
        return new EstadisticasCamara(ip, 0)
        {
            EstaConectada = false,
            UltimoMensaje = "Sin conexión",
            HoraUltimoMensaje = DateTime.UtcNow
        };
    });

    return Results.Ok(resultado);
}

    private static async Task<IResult> AgregarCamara(
        IGestorEndpointsCamaras gestor,
        [FromBody] CamaraRequest request)
    {
        try
        {
            var resultado = await gestor.AgregarCamara(request.IpCamara, request.Puerto);
            return resultado 
                ? Results.Ok(new { mensaje = "Cámara agregada correctamente" })
                : Results.BadRequest(new { error = "La cámara ya existe" });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> EliminarCamara(
        IGestorEndpointsCamaras gestor,
        string ip)
    {
        var resultado = await gestor.EliminarCamara(ip);
        return resultado
            ? Results.Ok(new { mensaje = "Cámara eliminada correctamente" })
            : Results.NotFound(new { error = "Cámara no encontrada" });
    }
}

public record CamaraRequest(string IpCamara, int Puerto);