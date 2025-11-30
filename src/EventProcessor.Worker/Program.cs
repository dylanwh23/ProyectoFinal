using EventProcessor.Worker;
using EventProcessor.Worker.Data;
using EventProcessor.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Config;

var builder = Host.CreateApplicationBuilder(args);

// Configuracion
builder.Services.Configure<RabbitMQConfig>(builder.Configuration.GetSection("RabbitMQ"));

// Base de datos SQLite
builder.Services.AddDbContext<EventDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("EventDatabase")),
    contextLifetime: ServiceLifetime.Scoped);

// HttpClient para CameraDiscoveryService
builder.Services.AddHttpClient();

// Servicios
builder.Services.AddSingleton<VideoLinkService>();
builder.Services.AddSingleton<JsonExportService>();
builder.Services.AddSingleton<EventProcessorService>();
builder.Services.AddScoped<EventStorageService>();
builder.Services.AddSingleton<CameraDiscoveryService>();
builder.Services.AddSingleton<DynamicRabbitMQConsumerService>();
builder.Services.AddSingleton<SimpleHttpServerService>();
builder.Services.AddSingleton<EventSimulatorService>(); // Comentar/Descomentar para activar/desactivar simulador

// Hosted Services
builder.Services.AddHostedService(provider => provider.GetRequiredService<DynamicRabbitMQConsumerService>());
builder.Services.AddHostedService<SimpleHttpServerService>();
builder.Services.AddHostedService<EventSimulatorService>(); // Comentar/Descomentar para activar/desactivar simulador
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Crear base de datos si no existe y mostrar estructura
using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<EventDbContext>();
    dbContext.Database.EnsureCreated();
    Console.WriteLine("✅ SQLite database created/verified");

    // Mostrar estructura de la base de datos
    Console.WriteLine("📊 DATABASE STRUCTURE:");
    Console.WriteLine($"Tables: {string.Join(", ", dbContext.Model.GetEntityTypes().Select(e => e.GetTableName()))}");

    var eventsTable = dbContext.Model.FindEntityType(typeof(Shared.Contracts.Models.EnrichedEvent));
    if (eventsTable != null)
    {
        Console.WriteLine("📋 Events Table Columns:");
        foreach (var property in eventsTable.GetProperties())
        {
            Console.WriteLine($"  - {property.Name} ({property.ClrType.Name})");
        }
    }

    // Contar eventos existentes
    var eventCount = dbContext.Set<Shared.Contracts.Models.EnrichedEvent>().Count();
    Console.WriteLine($"📈 Total events in database: {eventCount}");
}

// Inicializar carpeta de exportación JSON
using (var scope = host.Services.CreateScope())
{
    var jsonExportService = scope.ServiceProvider.GetRequiredService<JsonExportService>();
    Console.WriteLine("✅ Servicio de exportación JSON inicializado");
    Console.WriteLine("🌐 Servidor HTTP iniciará en: http://localhost:5005");
}


await host.StartAsync();

Console.WriteLine("🚀 Aplicación iniciada. Presiona Ctrl+C para detener...");

// Esperar a que se presione Ctrl+C
var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
};

await Task.Delay(Timeout.Infinite, cancellationTokenSource.Token);

// Cuando se presiona Ctrl+C, detener la aplicación
Console.WriteLine("🛑 Deteniendo aplicación...");
await host.StopAsync();

Console.WriteLine("🎯 Aplicación detenida. Presiona ENTER para cerrar la ventana...");
Console.ReadLine();