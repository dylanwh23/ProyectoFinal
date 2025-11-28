using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelnetInterceptor.Worker.Models; // Asegúrate de tener HistorySnapshot aquí

namespace TelnetInterceptor.Worker.Services;

public class EventosService
{
    private readonly ILogger<EventosService> _logger;
    private readonly CameraStreamService _cameraService;
    private readonly string _eventosOutputPath;

    public EventosService(
        ILogger<EventosService> logger,
        CameraStreamService cameraService,
        IOptions<ServerSettings> settings) // Mantenemos ServerSettings para saber dónde guardar los eventos generados
    {
        _logger = logger;
        _cameraService = cameraService;

        // Definimos dónde se guardarán los reportes de eventos.
        // Si ServerSettings tiene una ruta, usamos esa + "\Eventos", sino una por defecto.
        var basePath = !string.IsNullOrEmpty(settings.Value.WatchPath) 
            ? settings.Value.WatchPath 
            : "C:\\TelnetInterceptor_Data";

        // Aseguramos que no sea la raíz de una unidad si es posible
        if (Path.GetFileName(basePath) == "") basePath = Path.Combine(basePath, "Data");

        _eventosOutputPath = Path.Combine(basePath, "EventosGenerados");

        if (!Directory.Exists(_eventosOutputPath))
        {
            Directory.CreateDirectory(_eventosOutputPath);
            _logger.LogInformation("📁 Carpeta de eventos configurada en: {path}", _eventosOutputPath);
        }
    }

    // --- LISTAR EVENTOS (RESTAURADO) ---
    public List<EventoGuardado> ListarEventos()
    {
        var eventos = new List<EventoGuardado>();

        if (!Directory.Exists(_eventosOutputPath)) return eventos;

        // Cada evento es una subcarpeta
        var directorios = Directory.GetDirectories(_eventosOutputPath);

        foreach (var dir in directorios)
        {
            // Buscamos el archivo metadata.json dentro de la carpeta
            var jsonPath = Path.Combine(dir, "metadata.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    var json = File.ReadAllText(jsonPath);
                    var evento = JsonSerializer.Deserialize<EventoGuardado>(json);
                    if (evento != null)
                    {
                        eventos.Add(evento);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error leyendo metadata de evento en {dir}: {msg}", dir, ex.Message);
                }
            }
        }

        return eventos.OrderByDescending(e => e.FechaCreacion).ToList();
    }

    public async Task<EventoGuardado?> CrearEvento(
        string ipCamara,
        int desdeSegundos,
        int hastaSegundos,
        string? nombre = null,
        string? descripcion = null)
    {
        _logger.LogInformation("🎬 Creando evento para {camera} (-{desde}s a -{hasta}s)", ipCamara, desdeSegundos, hastaSegundos);

        // 1. Calcular rango de tiempo
        var ahora = DateTime.Now;
        var inicio = ahora.AddSeconds(-desdeSegundos);
        var fin = ahora.AddSeconds(-hastaSegundos);

        // 2. Obtener snapshots usando el servicio de cámaras (que sabe dónde está cada cámara)
        var snapshot = _cameraService.FreezeHistoryByTimeRangeLocal(ipCamara, inicio, fin);

        if (snapshot.Files.Count == 0)
        {
            _logger.LogWarning("No se encontraron imágenes para el evento en {ip}", ipCamara);
            return null;
        }

        // 3. Crear estructura de carpetas para el evento
        var eventoId = Guid.NewGuid().ToString();
        var carpetaEvento = Path.Combine(_eventosOutputPath, eventoId);
        Directory.CreateDirectory(carpetaEvento);

        var imagenesEvento = new List<ImagenEvento>();

        // 4. Copiar imágenes
        foreach (var file in snapshot.Files)
        {
            try
            {
                var nombreArchivo = Path.GetFileName(file);
                var destino = Path.Combine(carpetaEvento, nombreArchivo);
                File.Copy(file, destino);
                imagenesEvento.Add(new ImagenEvento { RutaRelativa = nombreArchivo });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Fallo al copiar imagen {img}: {msg}", file, ex.Message);
            }
        }

        // 5. Crear objeto de metadatos
        var nuevoEvento = new EventoGuardado
        {
            EventoId = eventoId,
            Nombre = nombre ?? $"Evento {ahora:yyyy-MM-dd HH:mm:ss}",
            Descripcion = descripcion,
            CameraName = ipCamara,
            Desde = desdeSegundos,
            Hasta = hastaSegundos,
            CantidadImagenes = imagenesEvento.Count,
            FechaCreacion = ahora,
            Imagenes = imagenesEvento
        };

        // 6. Guardar metadata.json
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(nuevoEvento, jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(carpetaEvento, "metadata.json"), jsonString);

        return nuevoEvento;
    }

    public bool EliminarEvento(string eventoId)
    {
        var eventoPath = Path.Combine(_eventosOutputPath, eventoId);

        if (!Directory.Exists(eventoPath)) return false;

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

// Modelos para el JSON del evento
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
    public string RutaRelativa { get; set; } = string.Empty;
}