using ImageInterceptor.Worker; // Asegúrate que coincida con tu namespace

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        // 1. Obtenemos la configuración
        IConfiguration configuration = hostContext.Configuration;

        // 2. Registramos nuestra clase de settings
        services.Configure<ImageWatcherSettings>(configuration.GetSection("ImageWatcherSettings"));
        services.Configure<CleanupSettings>(configuration.GetSection("CleanupSettings"));

        // 3. Registramos el worker
        services.AddHostedService<Worker>();

        // 4. (¡IMPORTANTE!) Ya no necesitamos AddHttpClient(), así que lo quitamos.
    })
    .Build();

host.Run();