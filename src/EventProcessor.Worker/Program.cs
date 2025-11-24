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
    contextLifetime: ServiceLifetime.Singleton);

// Servicios
builder.Services.AddSingleton<VideoLinkService>();
builder.Services.AddSingleton<EventProcessorService>();
builder.Services.AddSingleton<EventStorageService>();
builder.Services.AddSingleton<RabbitMQConsumerService>();
// builder.Services.AddSingleton<EventSimulatorService>(); // Simulador de eventos

// Hosted Services
builder.Services.AddHostedService(provider => provider.GetRequiredService<RabbitMQConsumerService>());
// builder.Services.AddHostedService(provider => provider.GetRequiredService<EventSimulatorService>()); // Simulador de eventos
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

await host.RunAsync();
