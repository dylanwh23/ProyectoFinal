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

    // =========================================================
    // 6. MÉTODO DE INTEGRACIÓN CON EL CONTROLADOR
    // =========================================================

    public string? ObtenerRutaCarpeta(string identificador)
    {
        // 1. Buscamos si el identificador es una IP que ya estamos vigilando
        if (_rutasActivas.TryGetValue(identificador, out var ruta))
        {
            return ruta;
        }

        // 2. Si no es IP (es un Nombre como "Camara1"), tenemos que buscar en la BD.
        // Como esto se llama desde un Controller, creamos un scope rápido.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var camara = db.Eventos.FirstOrDefault(c => c.Nombre == identificador);
            return camara?.RutaCarpeta;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error buscando ruta para {Id}: {Msg}", identificador, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Obtiene las últimas 'count' imágenes basándose en el número secuencial del nombre.
    /// Vital para el botón de PAUSA.
    /// </summary>
    public HistorySnapshot GetRecentFrames(string identificador, int count)
    {
        // CAMBIO: Usamos el método inteligente que busca en memoria o BD
        var ruta = ObtenerRutaCarpeta(identificador);

        if (string.IsNullOrEmpty(ruta) || !Directory.Exists(ruta))
            return new HistorySnapshot { HistoryId = "Error: Cámara no encontrada o ruta inválida" };

        var files = new List<string>();

        // El resto de la lógica de archivos se mantiene igual, pero ahora 'ruta' es correcta
        var allFiles = Directory.GetFiles(ruta, "*.*")
            .Where(f => IsImage(f));

        var filtered = allFiles
            .Select(f => new { Path = f, Number = ExtractNumber(Path.GetFileNameWithoutExtension(f)) })
            .Where(x => x.Number != -1)
            .OrderByDescending(x => x.Number)
            .Take(count)
            .OrderBy(x => x.Number)
            .Select(x => x.Path)
            .ToList();

        files.AddRange(filtered);

        return new HistorySnapshot
        {
            HistoryId = "Buffer",
            Files = files
        };
    }

    // =========================================================
    // 2. NUEVOS MÉTODOS PARA RANGO DE IMÁGENES
    // =========================================================

    /// <summary>
    /// Obtiene imágenes dentro de un rango de números (ej: 1100 a 1200)
    /// </summary>
    public HistorySnapshot GetFramesByRange(string identificador, int fromNumber, int toNumber)
    {
        // CAMBIO: Usamos ObtenerRutaCarpeta
        var ruta = ObtenerRutaCarpeta(identificador);

        if (string.IsNullOrEmpty(ruta) || !Directory.Exists(ruta))
            return new HistorySnapshot { HistoryId = "Error: Cámara no encontrada o ruta inválida" };

        var files = new List<string>();

        var allFiles = Directory.GetFiles(ruta, "*.*")
            .Where(f => IsImage(f));

        var filtered = allFiles
            .Select(f => new { Path = f, Number = ExtractNumber(Path.GetFileNameWithoutExtension(f)) })
            .Where(x => x.Number >= fromNumber && x.Number <= toNumber)
            .OrderBy(x => x.Number)
            .Select(x => x.Path)
            .ToList();

        files.AddRange(filtered);

        return new HistorySnapshot
        {
            HistoryId = $"Range_{fromNumber}-{toNumber}",
            Files = files,
            FromNumber = fromNumber,
            ToNumber = toNumber
        };
    }

    /// <summary>
    /// Obtiene información del rango disponible (mínimo y máximo número de imagen)
    /// </summary>
    public RangeInfo? GetAvailableRange(string identificador)
    {
        // CAMBIO: Usamos ObtenerRutaCarpeta
        var ruta = ObtenerRutaCarpeta(identificador);

        if (string.IsNullOrEmpty(ruta) || !Directory.Exists(ruta))
            return null;

        var allFiles = Directory.GetFiles(ruta, "*.*")
            .Where(f => IsImage(f))
            .Select(f => new { Path = f, Number = ExtractNumber(Path.GetFileNameWithoutExtension(f)) })
            .Where(x => x.Number != -1)
            .ToList();


        if (allFiles.Count == 0)
            return null;

        // Recalcular MinNumber y MaxNumber en base a todos los archivos en el directorio
        var actualMinNumber = allFiles.Min(x => x.Number);
        var actualMaxNumber = allFiles.Max(x => x.Number);


        return new RangeInfo
        {
            CameraIp = identificador, // Devolvemos el identificador usado (Nombre o IP)
            MinNumber = actualMinNumber,
            MaxNumber = actualMaxNumber,
            TotalFiles = allFiles.Count,
            FolderPath = ruta
        };
    }

    // =========================================================
    // 3. GESTIÓN DE CÁMARAS (BASE DE DATOS)
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
        EliminarWatcher(ip);
        return true;
    }

    // =========================================================
    // 4. WATCHERS (VIGILANCIA EN SEGUNDO PLANO)
    // =========================================================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🟢 CameraManagerService Iniciado");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SincronizarWatchers();
                await Task.Delay(5000, stoppingToken);
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

        foreach (var ip in _watchers.Keys)
        {
            if (!ipsBd.Contains(ip)) EliminarWatcher(ip);
        }

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
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            w.Created += (s, e) => ProcesarArchivo(ip, e.FullPath);
            w.Changed += (s, e) => ProcesarArchivo(ip, e.FullPath);

            _watchers[ip] = w;
            _rutasActivas[ip] = ruta;

            var lastFile = new DirectoryInfo(ruta).GetFiles("*.*")
                .Where(f => IsImage(f.Name))
                .Select(f => new { File = f, Number = ExtractNumber(Path.GetFileNameWithoutExtension(f.Name)) })
                .OrderByDescending(x => x.Number)
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
            _lastImageTime[ip] = DateTime.UtcNow;
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
    // 5. HELPERS DE PARSEO
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
                if (int.TryParse(matches[^1].Value, out int number)) return number;
            }
        }
        catch { }
        return -1;
    }

    /// <summary>
    /// Determina el rango de frames (FromFrame y ToFrame) para un evento,
    /// basándose en un frame central y una cantidad de frames adyacentes (antes y después).
    /// </summary>
    /// <param name="centerFramePath">La ruta completa del frame central del evento.</param>
    /// <param name="framesBefore">Número de frames a incluir antes del frame central.</param>
    /// <param name="framesAfter">Número de frames a incluir después del frame central.</param>
    /// <returns>Un objeto EventFrameRange con FromFrame, ToFrame y FolderPath, o null si no se puede determinar.</returns>
    public EventFrameRange? GetFrameRangeForEvent(string centerFramePath, int framesBefore, int framesAfter)
    {
        // 1. Extraer el número del nombre del archivo del centerFramePath
        var centerFrameFileName = Path.GetFileNameWithoutExtension(centerFramePath);
        var centerFrameNumber = ExtractNumber(centerFrameFileName);

        if (centerFrameNumber == -1)
        {
            _logger.LogWarning("No se pudo extraer el número del frame central: {Path}", centerFramePath);
            return null;
        }


        // 2. Obtener la ruta de la carpeta del centerFramePath y el identificador de la cámara
        var folderPath = Path.GetDirectoryName(centerFramePath);
        if (string.IsNullOrEmpty(folderPath))
        {
            _logger.LogWarning("No se pudo obtener la ruta de la carpeta del frame central: {Path}", centerFramePath);
            return null;
        }

        // Buscar el identificador de la cámara (IP o Nombre) a partir de la ruta de la carpeta
        var cameraIpOrName = _rutasActivas.FirstOrDefault(x => x.Value == folderPath).Key;
        if (string.IsNullOrEmpty(cameraIpOrName))
        {
            _logger.LogWarning("No se encontró cámara activa para la ruta: {FolderPath}", folderPath);
            // Intentar buscar en la BD si la ruta pertenece a una cámara registrada
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var camara = db.Eventos.FirstOrDefault(c => c.RutaCarpeta == folderPath);
            if (camara != null)
            {
                cameraIpOrName = camara.IpCamara; // Usamos la IP de la cámara encontrada en BD
            }
            else
            {
                return null;
            }
        }


        // 3. Obtener el rango disponible de frames (MinNumber, MaxNumber) para esa cámara
        var rangeInfo = GetAvailableRange(cameraIpOrName);

        if (rangeInfo == null)
        {
            _logger.LogWarning("No se pudo obtener el rango disponible para la cámara: {Id}", cameraIpOrName);
            return null;
        }


        // 4. Calcular fromFrame y toFrame
        var fromFrame = Math.Max(rangeInfo.MinNumber, centerFrameNumber - framesBefore);
        
        int toFrame;
        // Si el número máximo de frames disponibles en disco (rangeInfo.MaxNumber)
        // es menor que el frame central más la cantidad de frames deseados después (framesAfter),
        // asumimos que los frames "futuros" se generarán y establecemos toFrame directamente a centerFrameNumber + framesAfter.
        if (rangeInfo.MaxNumber < (centerFrameNumber + framesAfter))
        {
            toFrame = centerFrameNumber + framesAfter;
        }
        else
        {
            // Si ya hay suficientes frames en disco (o más de los que necesitamos),
            // entonces limitamos el toFrame al deseado, sin exceder el MaxNumber real.
            toFrame = Math.Min(rangeInfo.MaxNumber, centerFrameNumber + framesAfter);
        }

        return new EventFrameRange
        {
            FromFrame = fromFrame,
            ToFrame = toFrame,
            FolderPath = folderPath
        };
    }
}




// =========================================================
// NUEVO MODELO: Información de rango disponible
// =========================================================
public class RangeInfo
{
    public string CameraIp { get; set; } = string.Empty;
    public int MinNumber { get; set; }
    public int MaxNumber { get; set; }
    public int TotalFiles { get; set; }
    public string FolderPath { get; set; } = string.Empty;
}

// =========================================================
// NUEVO MODELO: Rango de frames para eventos
// =========================================================
public class EventFrameRange
{
    public int FromFrame { get; set; }
    public int ToFrame { get; set; }
    public string FolderPath { get; set; } = string.Empty;
}
