using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelnetInterceptor.Worker.Data;
using TelnetInterceptor.Worker.Models;

namespace TelnetInterceptor.Worker.Services;

public class CameraManagerService : BackgroundService
{
    private readonly ILogger<CameraManagerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    
    // Estado en Memoria (Watchers activos y últimas fotos)
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, string> _rutasActivas = new();
    private readonly ConcurrentDictionary<string, string> _latestFilePerCamera = new();
    
    // Ruta base para guardar eventos permanentes
    private readonly string _eventosOutputPath = @"C:\TelnetInterceptor_Data\EventosGenerados";

    public CameraManagerService(
        ILogger<CameraManagerService> logger, 
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        
        if (!Directory.Exists(_eventosOutputPath)) 
            Directory.CreateDirectory(_eventosOutputPath);
    }

    // =========================================================
    // 1. GESTIÓN DE BASE DE DATOS (CRUD)
    // =========================================================

    public async Task<List<EstadisticasCamara>> ObtenerCamarasBd()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Eventos.ToListAsync(); 
    }

    public async Task<bool> AgregarCamara(string ip, int puerto, string ruta)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Validación básica
        if (await db.Eventos.AnyAsync(c => c.IpCamara == ip)) return false;

        var nueva = new EstadisticasCamara(ip, puerto, ruta);
        db.Eventos.Add(nueva);
        await db.SaveChangesAsync();
        
        // Forzamos sincronización inmediata de watchers
        await SincronizarWatchers(); 
        return true;
    }

    public async Task<bool> EliminarCamara(string ip)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var target = await db.Eventos.FirstOrDefaultAsync(c => c.IpCamara == ip);
        if (target == null) return false;

        db.Eventos.Remove(target);
        await db.SaveChangesAsync();

        // Limpiamos recursos en memoria
        EliminarWatcher(ip);
        return true;
    }

    // =========================================================
    // 2. LÓGICA DE WATCHERS (BACKGROUND)
    // =========================================================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🟢 CameraManagerService Iniciado (Gestión + Imágenes)");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SincronizarWatchers();
                // Revisa cambios en la BD cada 5 segundos
                await Task.Delay(5000, stoppingToken); 
            }
            catch (Exception ex)
            {
                _logger.LogError("Error en ciclo de Watchers: {msg}", ex.Message);
                await Task.Delay(10000, stoppingToken);
            }
        }
    }

    private async Task SincronizarWatchers()
    {
        var camaras = await ObtenerCamarasBd();
        var ipsBd = camaras.Select(c => c.IpCamara).ToHashSet();

        // A. Eliminar watchers de cámaras que ya no están en BD
        foreach (var ip in _watchers.Keys)
        {
            if (!ipsBd.Contains(ip)) EliminarWatcher(ip);
        }

        // B. Crear o actualizar watchers
        foreach (var cam in camaras)
        {
            if (string.IsNullOrWhiteSpace(cam.RutaCarpeta)) continue;

            // Si ya existe, verificamos si cambió la ruta
            if (_rutasActivas.TryGetValue(cam.IpCamara, out var rutaActual))
            {
                if (rutaActual != cam.RutaCarpeta)
                {
                    _logger.LogInformation("🔄 Ruta cambiada para {Ip}. Reiniciando watcher.", cam.IpCamara);
                    EliminarWatcher(cam.IpCamara);
                    IniciarWatcher(cam.IpCamara, cam.RutaCarpeta);
                }
            }
            else
            {
                // Es nueva
                IniciarWatcher(cam.IpCamara, cam.RutaCarpeta);
            }
        }
    }

    private void IniciarWatcher(string ip, string ruta)
    {
        try
        {
            if (!Directory.Exists(ruta)) Directory.CreateDirectory(ruta);

            var w = new FileSystemWatcher(ruta, "*.jpg") 
            { 
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true 
            };
            
            w.Created += (s, e) => _latestFilePerCamera[ip] = e.FullPath;
            
            _watchers[ip] = w;
            _rutasActivas[ip] = ruta;
            _logger.LogInformation("👀 Vigilando: {Ip} -> {Ruta}", ip, ruta);
        }
        catch (Exception ex) 
        { 
            _logger.LogError("No se pudo iniciar watcher para {Ip}: {Msg}", ip, ex.Message); 
        }
    }

    private void EliminarWatcher(string ip)
    {
        if (_watchers.TryRemove(ip, out var w)) 
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _rutasActivas.TryRemove(ip, out _);
        _latestFilePerCamera.TryRemove(ip, out _);
    }

    // =========================================================
    // 3. STREAMING E HISTORIAL TEMPORAL
    // =========================================================

    public string? GetLatestFile(string ip) 
    {
        _latestFilePerCamera.TryGetValue(ip, out var f);
        return f;
    }

    public HistorySnapshot FreezeHistory(string ip, DateTime start, DateTime end)
    {
        if (!_rutasActivas.TryGetValue(ip, out var ruta)) 
            return new HistorySnapshot { HistoryId = "Error: Cámara no activa" };

        var files = new List<string>();
        if (Directory.Exists(ruta))
        {
            files = Directory.GetFiles(ruta, "*.jpg")
                .Where(f => File.GetCreationTime(f) >= start && File.GetCreationTime(f) <= end)
                .OrderBy(f => File.GetCreationTime(f))
                .ToList();
        }

        return new HistorySnapshot 
        { 
            HistoryId = Guid.NewGuid().ToString(), 
            Files = files 
        };
    }

    // =========================================================
    // 4. EVENTOS PERMANENTES
    // =========================================================

    public async Task<EventoGuardado?> CrearEventoPermanente(string ip, int segAntes, int segDespues, string nombre)
    {
        var ahora = DateTime.Now;
        // Obtenemos snapshot temporal
        var snapshot = FreezeHistory(ip, ahora.AddSeconds(-segAntes), ahora.AddSeconds(-segDespues));
        
        if (snapshot.Files.Count == 0) return null;

        // Creamos carpeta permanente
        var eventoId = Guid.NewGuid().ToString();
        var carpetaDestino = Path.Combine(_eventosOutputPath, eventoId);
        Directory.CreateDirectory(carpetaDestino);

        var imagenes = new List<ImagenEvento>();

        // Copiamos archivos
        foreach (var file in snapshot.Files)
        {
            var nombreArchivo = Path.GetFileName(file);
            var dest = Path.Combine(carpetaDestino, nombreArchivo);
            File.Copy(file, dest, true);
            imagenes.Add(new ImagenEvento { RutaRelativa = nombreArchivo });
        }

        var evento = new EventoGuardado 
        { 
            EventoId = eventoId, 
            CameraName = ip, 
            Imagenes = imagenes, 
            FechaCreacion = ahora,
            Nombre = nombre ?? $"Evento {ahora:HH:mm:ss}",
            Desde = segAntes,
            Hasta = segDespues,
            CantidadImagenes = imagenes.Count
        };
        
        // Guardamos metadata.json
        var json = JsonSerializer.Serialize(evento, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(carpetaDestino, "metadata.json"), json);

        return evento;
    }

    public List<EventoGuardado> ListarEventosGuardados()
    {
        var lista = new List<EventoGuardado>();
        if (!Directory.Exists(_eventosOutputPath)) return lista;
        
        foreach(var dir in Directory.GetDirectories(_eventosOutputPath))
        {
            var meta = Path.Combine(dir, "metadata.json");
            if(File.Exists(meta)) 
            {
                try {
                    var obj = JsonSerializer.Deserialize<EventoGuardado>(File.ReadAllText(meta));
                    if(obj != null) lista.Add(obj);
                } catch {}
            }
        }
        return lista.OrderByDescending(x => x.FechaCreacion).ToList();
    }
}

public class EventoGuardado
{
    public string EventoId { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string CameraName { get; set; } = string.Empty; // IP de la cámara
    public int Desde { get; set; } // Segundos antes
    public int Hasta { get; set; } // Segundos después
    public int CantidadImagenes { get; set; }
    public DateTime FechaCreacion { get; set; }
    public List<ImagenEvento> Imagenes { get; set; } = new();
}

public class ImagenEvento
{
    public string RutaRelativa { get; set; } = string.Empty;
}