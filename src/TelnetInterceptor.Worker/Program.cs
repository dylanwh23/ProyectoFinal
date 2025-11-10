using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TelnetInterceptor.Worker;
using TelnetInterceptor.Worker.Configuration;
using TelnetInterceptor.Worker.Services;
using TelnetInterceptor.Worker.Endpoints;
using MassTransit;
using Shared.Contracts;
using Microsoft.OpenApi.Models;
using RabbitMQ.Client;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic; // Added for List

var builder = WebApplication.CreateBuilder(args);

// --- Limpieza del exchange conflictivo ---
static async Task LimpiarExchangeConflictivoAsync(IConfiguration configuration)
{
    var rabbitMQConfig = configuration.GetSection("RabbitMQ");
    string rabbitHost = rabbitMQConfig["Host"] ?? "localhost";
    string exchangeName = typeof(EventoMovimientoDetectado).FullName!.Replace('.', ':');

    Console.WriteLine($"RabbitMQ Cleanup: Intentando limpiar Exchange: {exchangeName} en host: {rabbitHost}");

    try
    {
        var factory = new ConnectionFactory
        {
            HostName = rabbitHost,
            UserName = "guest",
            Password = "guest",
            ContinuationTimeout = TimeSpan.FromSeconds(5)
        };

        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        try
        {
            await channel.ExchangeDeleteAsync(exchangeName, ifUnused: false);
            Console.WriteLine($"✅ Exchange '{exchangeName}' eliminado con éxito.");
        }
        catch (Exception ex) when (ex.Message.Contains("NOT_FOUND") || ex.Message.Contains("404"))
        {
            Console.WriteLine($"Exchange '{exchangeName}' no encontrado. No requiere limpieza.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error de conexión a RabbitMQ: {ex.Message}");
    }
}

await LimpiarExchangeConflictivoAsync(builder.Configuration);

// 1️⃣ Servicios base
builder.Services.Configure<ConfiguracionInterceptor>(
    builder.Configuration.GetSection("ConfiguracionInterceptor"));

builder.Services.AddSingleton<IServicioFiltradoEventos, ServicioFiltradoEventos>();
builder.Services.AddSingleton<Worker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Worker>());

// 2️⃣ MassTransit (Publisher & Consumers)
var rabbitMQConfig = builder.Configuration.GetSection("RabbitMQ");
var rabbitHost = rabbitMQConfig["Host"] ?? "localhost";
var rabbitUser = rabbitMQConfig["Username"] ?? "guest";
var rabbitPass = rabbitMQConfig["Password"] ?? "guest";

// Get camera configurations
var configuracionInterceptor = builder.Configuration.GetSection("ConfiguracionInterceptor").Get<ConfiguracionInterceptor>();
var cameras = configuracionInterceptor?.Camaras ?? new List<ConfiguracionCamara>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<CameraDeletedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });

        // Configura un endpoint de recepción para el consumidor de eliminación de cámaras
        cfg.ReceiveEndpoint("camera-deleted-events", e =>
        {
            e.ConfigureConsumer<CameraDeletedConsumer>(context);
        });

        // Iterate through each camera and configure a consumer and receive endpoint
        /*foreach (var camera in cameras)
        {
            // Create a unique queue name for each camera IP
            var queueName = $"queue_evento_movimiento_{camera.IpCamara.Replace(".", "_")}";
            var ipCamara = camera.IpCamara; // Capture the current camera's IP

            cfg.ReceiveEndpoint(queueName, e =>
            {
                // Configure the consumer, injecting the specific ipCamara and logger
                e.Consumer<EventoMovimientoConsumer>(() =>
                    new EventoMovimientoConsumer(ipCamara, context.GetRequiredService<ILogger<EventoMovimientoConsumer>>()));

                // Optional: Configure Dead Lettering or other endpoint settings here
                // e.ConfigureDeadLettering();
            });
        }*/
    });
});

// 3️⃣ Registro del Gestor
builder.Services.AddSingleton<IGestorEndpointsCamaras, GestorEndpointsCamaras>();

// 4️⃣ Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Telnet Interceptor API",
        Version = "v1",
        Description = "Microservicio para interceptar y gestionar cámaras con MassTransit y RabbitMQ"
    });
});

var app = builder.Build();

// 5️⃣ Middleware y endpoints
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Telnet Interceptor API v1");
        c.RoutePrefix = "swagger";
    });
}

app.MapTelnetEndpoints();
app.MapCamaraEndpoints();

app.MapGet("/", () => Results.Ok("✅ TelnetInterceptor Worker corriendo con Swagger y gestión de cámaras."));

await app.RunAsync();
