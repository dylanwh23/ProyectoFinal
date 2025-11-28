using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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
    
    // Estado en Memoria
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, string> _rutasActivas = new();
    
    // Puntero a la última foto (Ruta completa)
    private readonly ConcurrentDictionary<string, string> _latestFilePerCamera = new();
    
    // Hora UTC de la última foto recibida (Para la alerta de "Sin Señal")
    private readonly ConcurrentDictionary<string, DateTime> _lastImageTime = new();

    public CameraManagerService(
        ILogger<CameraManagerService> logger, 
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // =========================================================
    // 1. MÉTODOS DE LECTURA (STREAM, BUFFER, SALUD)
    // =========================================================

    public string? GetLatestFile(string ip) 
    {
        _latestFilePerCamera.TryGetValue(ip, out var f);
        return f;
    }

    public DateTime? GetLastImageTime(string ip)
    {
        if (_lastImageTime.TryGetValue(ip, out var time)) return time;
        return null;
    }

    /// <summary>
    /// Obtiene las últimas 'count' imágenes basándose en el número secuencial del nombre.
    /// Vital para el botón de PAUSA.
    /// </summary>
    public HistorySnapshot GetRecentFrames(string ip, int count)
    {
        if (!_rutasActivas.TryGetValue(ip, out var ruta)) 
            return new HistorySnapshot { HistoryId = "Error: Cámara no activa" };

        var files = new List<string>();
        if (Directory.Exists(ruta))
        {
            var allFiles = Directory.GetFiles(ruta, "*.*")
                .Where(f => IsImage(f));

            var filtered = allFiles
                // Extraemos el número: "Snapshot_500.bmp" -> 500
                .Select(f => new { Path = f, Number = ExtractNumber(Path.GetFileNameWithoutExtension(f)) })
                .Where(x => x.Number != -1) // Ignoramos archivos sin número
                .OrderByDescending(x => x.Number) // Ordenamos del más nuevo al más viejo
                .Take(count) // Tomamos los últimos N
                .OrderBy(x => x.Number) // Reordenamos ascendente para la reproducción
                .Select(x => x.Path)
                .ToList();

            files.AddRange(filtered);
        }

        return new HistorySnapshot 
        { 
            HistoryId = "Buffer", 
            Files = files 
        };
    }

    // =========================================================
    // 2. GESTIÓN DE CÁMARAS (BASE DE DATOS)
    // =========================================================

    public async Task<List<EstadisticasCamara>> ObtenerCamarasBd()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Eventos.ToListAsync(); 
    }

    public async Task<bool> AgregarCamara(string ip, int puerto, string ruta, string nombre)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.Eventos.AnyAsync(c => c.IpCamara == ip)) return false;

        db.Eventos.Add(new EstadisticasCamara(ip, puerto, ruta, nombre));
        await db.SaveChangesAsync();
        await SincronizarWatchers(); // Aplicar cambios ya
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
        EliminarWatcher(ip);
        return true;
    }

    // =========================================================
    // 3. WATCHERS (VIGILANCIA EN SEGUNDO PLANO)
    // =========================================================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🟢 CameraManagerService Iniciado");
        while (!stoppingToken.IsCancellationRequested)
        {
            try 
            { 
                await SincronizarWatchers(); 
                await Task.Delay(5000, stoppingToken); // Revisar BD cada 5s
            }
            catch (Exception ex)
            {
                _logger.LogError("Error en ciclo de vigilancia: {Msg}", ex.Message);
                await Task.Delay(10000, stoppingToken);
            }
        }
    }

    private async Task SincronizarWatchers()
    {
        var camaras = await ObtenerCamarasBd();
        var ipsBd = camaras.Select(c => c.IpCamara).ToHashSet();

        // Eliminar watchers de cámaras borradas
        foreach (var ip in _watchers.Keys)
        {
            if (!ipsBd.Contains(ip)) EliminarWatcher(ip);
        }

        // Crear/Actualizar watchers
        foreach (var cam in camaras)
        {
            if (string.IsNullOrWhiteSpace(cam.RutaCarpeta)) continue;

            if (_rutasActivas.TryGetValue(cam.IpCamara, out var rutaActual))
            {
                if (rutaActual != cam.RutaCarpeta)
                {
                    _logger.LogInformation("🔄 Cambio de ruta para {Ip}", cam.IpCamara);
                    EliminarWatcher(cam.IpCamara);
                    IniciarWatcher(cam.IpCamara, cam.RutaCarpeta);
                }
            }
            else
            {
                IniciarWatcher(cam.IpCamara, cam.RutaCarpeta);
            }
        }
    }

    private void IniciarWatcher(string ip, string ruta)
    {
        try
        {
            if (!Directory.Exists(ruta)) Directory.CreateDirectory(ruta);

            var w = new FileSystemWatcher(ruta) 
            { 
                Filter = "*.*", // Para detectar .bmp y .jpg
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true 
            };
            
            w.Created += (s, e) => ProcesarArchivo(ip, e.FullPath);
            w.Changed += (s, e) => ProcesarArchivo(ip, e.FullPath);
            
            _watchers[ip] = w;
            _rutasActivas[ip] = ruta;

            // Precarga inicial: Buscar la última imagen que YA existe en la carpeta
            var lastFile = new DirectoryInfo(ruta).GetFiles("*.*")
                .Where(f => IsImage(f.Name))
                .Select(f => new { File = f, Number = ExtractNumber(Path.GetFileNameWithoutExtension(f.Name)) })
                .OrderByDescending(x => x.Number) // Ordenar por número, no por fecha
                .FirstOrDefault();

            if (lastFile != null)
            {
                _latestFilePerCamera[ip] = lastFile.File.FullName;
                _lastImageTime[ip] = DateTime.UtcNow;
                _logger.LogInformation("📸 Inicializado {Ip} con {File}", ip, lastFile.File.Name);
            }

            _logger.LogInformation("👀 Vigilando: {Ip} -> {Ruta}", ip, ruta);
        }
        catch (Exception ex) 
        { 
            _logger.LogError("Error watcher {Ip}: {Msg}", ip, ex.Message); 
        }
    }

    private void ProcesarArchivo(string ip, string path)
    {
        if (IsImage(path)) 
        {
            _latestFilePerCamera[ip] = path;
            _lastImageTime[ip] = DateTime.UtcNow; // Actualizamos el "pulso" para la alerta
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
        _lastImageTime.TryRemove(ip, out _);
    }

    // =========================================================
    // 4. HELPERS DE PARSEO
    // =========================================================

    private bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return ext == ".bmp" || ext == ".jpg" || ext == ".jpeg" || ext == ".png";
    }

    private int ExtractNumber(string filename)
    {
        try 
        {
            var matches = Regex.Matches(filename, @"\d+");
            if (matches.Count > 0)
            {
                // Tomamos el último número encontrado en el nombre
                if (int.TryParse(matches[^1].Value, out int number)) return number;
            }
        }
        catch { }
        return -1;
    }
}