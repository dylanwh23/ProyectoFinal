using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TelnetInterceptor.Worker.Services;

public class EventosService
{
    private readonly ILogger<EventosService> _logger;
    private readonly CameraStreamService _cameraService;
    private readonly string _eventosPath;

    public EventosService(
        ILogger<EventosService> logger,
        CameraStreamService cameraService,
        IOptions<ServerSettings> settings)
    {
        _logger = logger;
        _cameraService = cameraService;

        // Carpeta de eventos al mismo nivel que la carpeta de imágenes
        var basePath = Path.GetDirectoryName(settings.Value.WatchPath) ?? settings.Value.WatchPath;
        _eventosPath = Path.Combine(basePath, "Eventos");

        if (!Directory.Exists(_eventosPath))
        {
            Directory.CreateDirectory(_eventosPath);
            _logger.LogInformation("📁 Carpeta de eventos creada en: {path}", _eventosPath);
        }
    }

    public async Task<EventoGuardado?> CrearEvento(
        string cameraName,
        int desde,
        int hasta,
        string? nombre = null,
        string? descripcion = null)
    {
        _logger.LogInformation("🎬 Creando evento para {camera} del {desde} al {hasta}", cameraName, desde, hasta);

        // Obtener las imágenes del rango
        string cameraPath = _cameraService.GetCameraPath(cameraName);

        if (!Directory.Exists(cameraPath))
        {
            _logger.LogWarning("No se encontró la carpeta de la cámara: {path}", cameraPath);
            return null;
        }

        var dirInfo = new DirectoryInfo(cameraPath);
        var allFiles = dirInfo.GetFiles("*.bmp", SearchOption.TopDirectoryOnly);
        var filesInRange = new List<(FileInfo file, int number)>();

        // Buscar archivos en el rango
        foreach (var file in allFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file.Name);
            var parts = fileName.Split('_');

            if (parts.Length >= 2 && int.TryParse(parts[^1], out int snapshotNumber))
            {
                if (snapshotNumber >= desde && snapshotNumber <= hasta)
                {
                    filesInRange.Add((file, snapshotNumber));
                }
            }
        }

        if (filesInRange.Count == 0)
        {
            _logger.LogWarning("No se encontraron imágenes en el rango {desde}-{hasta}", desde, hasta);
            return null;
        }

        // Crear carpeta del evento
        var eventoId = $"evt_{cameraName}_{desde}_{hasta}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var eventoPath = Path.Combine(_eventosPath, eventoId);
        Directory.CreateDirectory(eventoPath);

        // Copiar las imágenes
        var sortedFiles = filesInRange.OrderBy(x => x.number).ToList();
        var imagenesCopiadas = new List<ImagenEvento>();

        for (int i = 0; i < sortedFiles.Count; i++)
        {
            var (file, number) = sortedFiles[i];
            var destFileName = $"{i:D4}_{file.Name}"; // 0000_Snapshot1_200.bmp
            var destPath = Path.Combine(eventoPath, destFileName);

            try
            {
                await Task.Run(() => file.CopyTo(destPath, overwrite: false));
                imagenesCopiadas.Add(new ImagenEvento
                {
                    Index = i,
                    FileName = destFileName,
                    OriginalNumber = number,
                    OriginalFileName = file.Name
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo copiar {file}", file.Name);
            }
        }

        // Crear metadata del evento
        var evento = new EventoGuardado
        {
            EventoId = eventoId,
            Nombre = nombre ?? $"Evento {cameraName} {desde}-{hasta}",
            Descripcion = descripcion,
            CameraName = cameraName,
            Desde = desde,
            Hasta = hasta,
            CantidadImagenes = imagenesCopiadas.Count,
            FechaCreacion = DateTime.UtcNow,
            Imagenes = imagenesCopiadas
        };

        // Guardar metadata
        var metadataPath = Path.Combine(eventoPath, "metadata.json");
        var json = JsonSerializer.Serialize(evento, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, json);

        _logger.LogInformation("✅ Evento {id} creado con {count} imágenes", eventoId, imagenesCopiadas.Count);

        return evento;
    }

    public List<EventoGuardado> ListarEventos(string? cameraName = null)
    {
        var eventos = new List<EventoGuardado>();

        if (!Directory.Exists(_eventosPath))
            return eventos;

        var eventoDirs = Directory.GetDirectories(_eventosPath);

        foreach (var dir in eventoDirs)
        {
            var metadataPath = Path.Combine(dir, "metadata.json");

            if (!File.Exists(metadataPath))
                continue;

            try
            {
                var json = File.ReadAllText(metadataPath);
                var evento = JsonSerializer.Deserialize<EventoGuardado>(json);

                if (evento != null)
                {
                    // Filtrar por cámara si se especifica
                    if (string.IsNullOrEmpty(cameraName) || evento.CameraName == cameraName)
                    {
                        eventos.Add(evento);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al leer metadata de {dir}", dir);
            }
        }

        return eventos.OrderByDescending(e => e.FechaCreacion).ToList();
    }

    public EventoGuardado? ObtenerEvento(string eventoId)
    {
        var eventoPath = Path.Combine(_eventosPath, eventoId);

        if (!Directory.Exists(eventoPath))
            return null;

        var metadataPath = Path.Combine(eventoPath, "metadata.json");

        if (!File.Exists(metadataPath))
            return null;

        try
        {
            var json = File.ReadAllText(metadataPath);
            return JsonSerializer.Deserialize<EventoGuardado>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al leer evento {id}", eventoId);
            return null;
        }
    }

    public byte[]? ObtenerImagenEvento(string eventoId, int index)
    {
        var evento = ObtenerEvento(eventoId);

        if (evento == null || index < 0 || index >= evento.Imagenes.Count)
            return null;

        var imagen = evento.Imagenes[index];
        var imagePath = Path.Combine(_eventosPath, eventoId, imagen.FileName);

        if (!File.Exists(imagePath))
            return null;

        try
        {
            return File.ReadAllBytes(imagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al leer imagen {index} del evento {id}", index, eventoId);
            return null;
        }
    }

    public bool EliminarEvento(string eventoId)
    {
        var eventoPath = Path.Combine(_eventosPath, eventoId);

        if (!Directory.Exists(eventoPath))
            return false;

        try
        {
            Directory.Delete(eventoPath, recursive: true);
            _logger.LogInformation("🗑️ Evento {id} eliminado", eventoId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar evento {id}", eventoId);
            return false;
        }
    }
}

public class EventoGuardado
{
    public string EventoId { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string CameraName { get; set; } = string.Empty;
    public int Desde { get; set; }
    public int Hasta { get; set; }
    public int CantidadImagenes { get; set; }
    public DateTime FechaCreacion { get; set; }
    public List<ImagenEvento> Imagenes { get; set; } = new();
}

public class ImagenEvento
{
    public int Index { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int OriginalNumber { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
}