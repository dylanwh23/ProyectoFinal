using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using RabbitMQ.Client;
using Microsoft.OpenApi.Models;

// Namespaces de tu proyecto
using TelnetInterceptor.Worker.Data;
using TelnetInterceptor.Worker.Services;
using TelnetInterceptor.Worker.Configuration;
using TelnetInterceptor.Worker.Models;
using TelnetInterceptor.Worker.Endpoints;
using Shared.Contracts;

var builder = WebApplication.CreateBuilder(args);

// ==================================================================
// 1️⃣ CONFIGURACIÓN DE SERVICIOS (DI)
// ==================================================================

// A. Base de Datos SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

// B. Servicios Unificados (Singleton + Hosted)
// --------------------------------------------------
// 1. CameraManager: Dueño de la BD, Watchers e Imágenes
builder.Services.AddSingleton<CameraManagerService>();
builder.Services.AddHostedService(p => p.GetRequiredService<CameraManagerService>());

// 2. TelnetWorker: Dueño de Conexiones TCP y RabbitMQ
builder.Services.AddSingleton<TelnetWorkerService>();
builder.Services.AddHostedService(p => p.GetRequiredService<TelnetWorkerService>());
// --------------------------------------------------

// C. Configuración Típada (Opcional, si usas IOptions)
builder.Services.Configure<ConfiguracionInterceptor>(
    builder.Configuration.GetSection("ConfiguracionInterceptor"));

// D. MassTransit (RabbitMQ)
var rabbitConf = builder.Configuration.GetSection("RabbitMQ");
builder.Services.AddMassTransit(x =>
{
    // Si tienes consumidores (como CameraDeletedConsumer), regístralos aquí:
    // x.AddConsumer<CameraDeletedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitConf["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rabbitConf["Username"] ?? "guest");
            h.Password(rabbitConf["Password"] ?? "guest");
        });
        
        // Configuración de endpoints de recepción si fuera necesario
        // cfg.ReceiveEndpoint("...", e => ... );
    });
});

// E. API Controllers y Swagger
builder.Services.AddControllers(); // Necesario para ImagenesController
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Telnet Interceptor API",
        Version = "v1",
        Description = "Sistema Unificado de Gestión de Cámaras y Eventos"
    });
});

var app = builder.Build();

// ==================================================================
// 2️⃣ MIDDLEWARE PIPELINE
// ==================================================================

// A. Swagger (Solo en desarrollo o si quieres verlo siempre)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
        c.RoutePrefix = "swagger"; // Accesible en /swagger
    });
}

// B. Archivos Estáticos (¡IMPORTANTE PARA TU FRONTEND!)
// UseDefaultFiles hará que al entrar a "/" te sirva "index.html"
app.UseDefaultFiles(); 
// UseStaticFiles permite servir los .css, .js y .html de wwwroot
app.UseStaticFiles();

// C. Mapeo de Rutas
app.MapControllers(); // Mapea ImagenesController

// D. Mapeo de Minimal APIs (Endpoints)
app.MapCamaraEndpoints();
app.MapTelnetEndpoints();
app.MapHistoryEndpoints();
app.MapEventosEndpoints();
// app.MapConfiguracionEndpoints(); // Si decides mantenerlo

// ==================================================================
// 3️⃣ ARRANQUE
// ==================================================================

// (Opcional) Migración automática al iniciar
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // db.Database.EnsureCreated(); // O db.Database.Migrate();
}

await app.RunAsync();