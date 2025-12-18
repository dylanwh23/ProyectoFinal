using EventProcessor.Worker;
using EventProcessor.Worker.Data;
using EventProcessor.Worker.HealthChecks;
using EventProcessor.Worker.Services;
using EventProcessor.Worker.Services.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using Serilog;

// ------------------------------------------------------------
// 1. CONFIGURACIÓN DE LOGGING (Serilog)
// ------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/eventprocessor-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("🚀 Iniciando EventProcessor Worker...");

    // ------------------------------------------------------------
    // 2. CONFIGURACIÓN DEL HOST
    // ------------------------------------------------------------
    var builder = Host.CreateApplicationBuilder(args);

    // Configuración de logging con Serilog
    builder.Services.AddLogging(loggingBuilder =>
    {
        loggingBuilder.ClearProviders();
        loggingBuilder.AddSerilog(Log.Logger, dispose: true);
    });

    // ------------------------------------------------------------
    // 3. CONFIGURACIÓN DE LA BASE DE DATOS
    // ------------------------------------------------------------
    var dbPath = Path.Combine(AppContext.BaseDirectory, "events.db");
    var connectionString = $"Data Source={dbPath};Cache=Shared";

    builder.Services.AddDbContext<EventDbContext>(options =>
    {
        options.UseSqlite(connectionString);
        options.EnableSensitiveDataLogging(false);
        options.EnableDetailedErrors(false);
    });

    // ------------------------------------------------------------
    // 4. HEALTH CHECKS
    // ------------------------------------------------------------
    builder.Services.AddHealthChecks()
        .AddCheck<DbContextHealthCheck<EventDbContext>>(
            name: "sqlite_db",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "database", "sqlite" })
        .AddCheck<RabbitMQHealthCheck>(
            name: "rabbitmq",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "messaging", "rabbitmq" });

    // ------------------------------------------------------------
    // 5. REGISTRO DE SERVICIOS
    // ------------------------------------------------------------

    // HttpClient para comunicaciones externas
    builder.Services.AddHttpClient("TelnetInterceptor", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "EventProcessor/1.0");
    });

    // Servicios singleton (compartidos)
    builder.Services.AddSingleton<CameraDiscoveryService>();
    builder.Services.AddSingleton<DynamicRabbitMQConsumerService>();
    builder.Services.AddSingleton<EventProcessorService>();
    builder.Services.AddSingleton<JsonExportService>();
    builder.Services.AddSingleton<EventStorageService>();
    builder.Services.AddSingleton<CleanupService>();
    builder.Services.AddSingleton<VideoLinkService>();
    builder.Services.AddSingleton<RabbitMQHealthCheck>();
    builder.Services.AddSingleton<WebhookService>();

    // Servicios hosted (BackgroundService)
    // builder.Services.AddHostedService<SimpleHttpServerService>();
    builder.Services.AddHostedService<DynamicRabbitMQConsumerService>();
    builder.Services.AddHostedService<Worker>();

    // Si necesitas el servicio de limpieza como hosted service
    builder.Services.AddHostedService<CleanupService>();

    // ------------------------------------------------------------
    // 6. CONFIGURACIÓN DE LA APLICACIÓN
    // ------------------------------------------------------------
    builder.Services.Configure<EventProcessorOptions>(builder.Configuration);
    builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhooks"));

    // ------------------------------------------------------------
    // 7. CONSTRUCCIÓN Y EJECUCIÓN
    // ------------------------------------------------------------
    var host = builder.Build();

    // Inicializar la base de datos
    using (var scope = host.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<EventDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        Log.Information("✅ Base de datos SQLite inicializada: {DbPath}", dbPath);
    }

    // Mostrar información de configuración
    Log.Information("========================================");
    Log.Information("🎯 EventProcessor Worker Configuration");
    Log.Information("========================================");
    Log.Information("📊 SQLite Database: {DbPath}", dbPath);
    Log.Information("🌐 TelnetInterceptor URL: {Url}",
        builder.Configuration["TelnetInterceptor:BaseUrl"] ?? "http://localhost:5000");
    Log.Information("🐇 RabbitMQ Host: {Host}",
        builder.Configuration["RabbitMQ:Host"] ?? "localhost");
    Log.Information("📁 JSON Export Folder: {Folder}",
        builder.Configuration["JsonExport:FolderPath"] ?? "./EventJsonExports");
    Log.Information("🔌 HTTP Server Port: {Port}",
        builder.Configuration["JsonExport:HttpPort"] ?? "5005");
    Log.Information("========================================");

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "❌ EventProcessor Worker terminó inesperadamente");
    throw;
}
finally
{
    Log.CloseAndFlush();
    await Task.Delay(1000); // Dar tiempo a que los logs se escriban
}