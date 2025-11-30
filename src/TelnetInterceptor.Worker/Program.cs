using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using TelnetInterceptor.Worker.Configuration;
using TelnetInterceptor.Worker.Data;
using TelnetInterceptor.Worker.Hubs;
using TelnetInterceptor.Worker.Services;
// La directiva using TelnetInterceptor.Worker.Services; ya existe, pero la aseguro para EventStorageService

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=app.db"));

builder.Services.AddSingleton<CameraManagerService>();
builder.Services.AddHostedService(p => p.GetRequiredService<CameraManagerService>());

builder.Services.AddScoped<IEventStorageService, EventStorageService>(); // Registrar EventStorageService
builder.Services.AddSingleton<TelnetWorkerService>();
builder.Services.AddHostedService(p => p.GetRequiredService<TelnetWorkerService>());

builder.Services.Configure<ConfiguracionInterceptor>(
    builder.Configuration.GetSection("ConfiguracionInterceptor"));

var rabbitConf = builder.Configuration.GetSection("RabbitMQ");
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitConf["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rabbitConf["Username"] ?? "guest");
            h.Password(rabbitConf["Password"] ?? "guest");
        });
    });
});

builder.Services.AddControllers(); 
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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
        c.RoutePrefix = "swagger"; // Accesible en /swagger
    });
}

app.UseDefaultFiles(); 
app.UseStaticFiles();
app.MapControllers();
app.MapHub<EventsHub>("/eventsHub");
await app.RunAsync();
