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
            .WithDescription("Obtiene la lista de cámaras registradas");

        group.MapGet("/estado", ObtenerEstadoCamaras)
            .WithDescription("Obtiene el estado actual (conexión y mensajes) de todas las cámaras");

        group.MapPost("/", AgregarCamara)
            .WithDescription("Registra una nueva cámara");

        group.MapDelete("/{ip}", EliminarCamara)
            .WithDescription("Elimina una cámara registrada");
    }

    private static async Task<IResult> ObtenerCamaras(CameraManagerService manager)
    {
        var camaras = await manager.ObtenerCamarasBd();
        return Results.Ok(camaras);
    }

    private static async Task<IResult> ObtenerEstadoCamaras(
        CameraManagerService manager, 
        TelnetWorkerService telnetWorker) // Inyectamos ambos para cruzar datos
    {
        // 1. Configuración desde BD (Manager)
        var camarasEnBd = await manager.ObtenerCamarasBd();
        
        // 2. Estado en vivo (TelnetWorker)
        var statsEnVivo = telnetWorker.ObtenerEstadisticas();

        // 3. Fusión
        var resultado = camarasEnBd.Select(cam => 
        {
            if (statsEnVivo.TryGetValue(cam.IpCamara, out var vivo)) return vivo;
            
            // Si no está conectada, devolvemos el objeto base desconectado
            return new EstadisticasCamara(cam.IpCamara, cam.Puerto, cam.RutaCarpeta)
            {
                EstaConectada = false,
                UltimoMensaje = "Desconectada / Sin Tráfico"
            };
        });

        return Results.Ok(resultado);
    }

    private static async Task<IResult> AgregarCamara(
        CameraManagerService manager,
        [FromBody] CamaraRequest request)
    {
        var exito = await manager.AgregarCamara(request.IpCamara, request.Puerto, request.RutaCarpeta);
        return exito 
            ? Results.Ok(new { mensaje = "Cámara agregada" }) 
            : Results.BadRequest(new { error = "La cámara ya existe" });
    }

    private static async Task<IResult> EliminarCamara(
        CameraManagerService manager,
        string ip)
    {
        var exito = await manager.EliminarCamara(ip);
        // También desconectamos del socket si estaba activa
        if (exito) 
        {
            // Nota: Aquí necesitaríamos inyectar TelnetWorkerService si queremos forzar desconexión inmediata,
            // pero el TelnetWorkerService se dará cuenta solo en su próximo ciclo de limpieza.
        }

        return exito
            ? Results.Ok(new { mensaje = "Cámara eliminada" })
            : Results.NotFound(new { error = "No encontrada" });
    }
}

public record CamaraRequest(string IpCamara, int Puerto, string RutaCarpeta);