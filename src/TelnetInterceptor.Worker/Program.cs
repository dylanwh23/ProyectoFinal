using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TelnetInterceptor.Worker;
using TelnetInterceptor.Worker.Configuration;
using TelnetInterceptor.Worker.Services;
using MassTransit;
using MassTransit.RabbitMqTransport;
using Shared.Contracts;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configuración: Carga la sección "ConfiguracionInterceptor"
builder.Services.Configure<ConfiguracionInterceptor>(
    builder.Configuration.GetSection("ConfiguracionInterceptor"));

// 2. Lógica Stateful: Inyecta el servicio de filtrado como SINGLETON. 
// Esto es crucial para mantener el estado (el diccionario) durante toda la vida del Worker.
builder.Services.AddSingleton<IServicioFiltradoEventos, ServicioFiltradoEventos>();

// 3. Servicio principal: El Worker que maneja las conexiones TCP
builder.Services.AddHostedService<Worker>();

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        // Configurar para usar fanout exchange (el tipo por defecto de MassTransit)
        cfg.Publish<EventoMovimientoDetectado>(e =>
        {
            e.ExchangeType = "fanout";
        });

        // Configurar el endpoint para la cola
        cfg.Send<EventoMovimientoDetectado>(e =>
        {
            e.UseRoutingKeyFormatter(context => "evento-movimiento-queue");
        });

        cfg.AutoStart = true;
    });
});


var host = builder.Build();
host.Run();