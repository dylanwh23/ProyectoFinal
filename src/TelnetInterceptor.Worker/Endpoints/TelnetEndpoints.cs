using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TelnetInterceptor.Worker.Models;

namespace TelnetInterceptor.Worker.Endpoints;

public static class TelnetEndpoints
{
    // NO recibe la instancia del Worker
    public static IEndpointRouteBuilder MapTelnetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/telnet")
            .WithTags("Telnet");

        // 1. Obtener estado de TODAS las conexiones
        group.MapGet("/status", (Worker worker) => // <-- Se inyecta aquí
        {
            return Results.Ok(worker.ObtenerEstadisticas());
        })
        .WithName("GetConnectionStatus")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Obtiene el estado de las conexiones Telnet de todas las cámaras"
        });

        return app;
    }
}
