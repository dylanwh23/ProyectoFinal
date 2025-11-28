using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TelnetInterceptor.Worker.Services;

namespace TelnetInterceptor.Worker.Endpoints;

public static class TelnetEndpoints
{
    public static IEndpointRouteBuilder MapTelnetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/telnet").WithTags("Telnet");

        group.MapGet("/status", (TelnetWorkerService worker) =>
        {
            return Results.Ok(worker.ObtenerEstadisticas());
        })
        .WithName("GetConnectionStatus")
        .WithDescription("Obtiene el estado de las conexiones Telnet de todas las cámaras");

        return app;
    }
}