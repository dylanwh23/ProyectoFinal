using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using System.Globalization;
using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Contracts;
using Shared.Contracts.Models; // Para AltaEventoModel
using TelnetInterceptor.Worker.Configuration;
// using TelnetInterceptor.Worker.Controllers; // No es necesario EventosController directamente
using TelnetInterceptor.Worker.Models;
using Microsoft.AspNetCore.SignalR;
using TelnetInterceptor.Worker.Hubs;

namespace TelnetInterceptor.Worker.Services;

public class TelnetWorkerService : BackgroundService
{
    private readonly ILogger<TelnetWorkerService> _logger;
    private readonly CameraManagerService _cameraManager;
    private readonly IBus _bus;
    private readonly ConfiguracionInterceptor _config;
    private readonly IServiceScopeFactory _scopeFactory; // Inyectar IServiceScopeFactory para crear un scope para el EventStorageService

    // Estado de Conexiones
    private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
    private readonly ConcurrentDictionary<string, EstadisticasCamara> _stats = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationSources = new();

    // Estado para "Cooldown" (Evitar spam de eventos)
    private readonly ConcurrentDictionary<string, string> _ultimoMsg = new();
    private readonly ConcurrentDictionary<string, DateTime> _ultimoTime = new();

    // Control de concurrencia
    private readonly ConcurrentDictionary<string, bool> _conectando = new();

    // Estado por cámara y estantería para inferir ALTA/BAJA/MOVIMIENTO
    // key: ipCamara -> (key: estanteria -> valor: conjunto de cajas presentes)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, HashSet<string>>> _estadoEstantesPorCamara = new();

    // Definición de estantes detectada por primer RAW (key: "ip:puerto" -> conjunto de nombres de estantes)
    private readonly ConcurrentDictionary<string, HashSet<string>> _gridEstantesDefinidosPorCamara = new();

    // Overrides de modo de cámara (grid, pallet, camion)
    private readonly ConcurrentDictionary<string, string> _cameraModeOverrides = new(StringComparer.OrdinalIgnoreCase);

    // Anti-falsos: confirmar cambios solo tras N lecturas consecutivas.
    private const int StabilityThreshold = 3;

    // key: "ip:puerto" -> key: "ESTANTE|CAJA" -> pending
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, GridPendingChange>> _gridPendingChanges = new();

    private sealed class GridPendingChange
    {
        public bool IntendedPresent { get; set; }
        public int Count { get; set; }
    }

    // Anti-falsos (CAMION): pending por sección hasta que sea estable N lecturas
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CamionPendingChange>> _camionPendingChanges = new();

    private sealed class CamionPendingChange
    {
        public string? IntendedCamionId { get; set; }
        public int Count { get; set; }
    }

    // Estado para modo pallet: contador de pallets y último estado por cámara
    private readonly ConcurrentDictionary<string, int> _palletCounters = new();
    private readonly ConcurrentDictionary<string, bool> _palletActivo = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _palletUltimasCajas = new();

    // Estado para modo camion: mapa de secciones reservadas y ocupantes actuales por cámara
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string?>> _camionEstado = new();

    private readonly IHubContext<EventsHub> _hub;

    public TelnetWorkerService(
        ILogger<TelnetWorkerService> logger,
        CameraManagerService cameraManager,
        IBus bus,
        IOptions<ConfiguracionInterceptor> config,
        IServiceScopeFactory scopeFactory,
        IHubContext<EventsHub> hub)
    {
        _logger = logger;
        _cameraManager = cameraManager;
        _bus = bus;
        _config = config.Value;
        _scopeFactory = scopeFactory;
        _hub = hub;
    }

    public void SetCameraMode(string ip, string? tipoEvento)
    {
        var mode = (tipoEvento ?? string.Empty).Trim().ToLowerInvariant();
        if (mode != "grid" && mode != "pallet" && mode != "camion")
        {
            _logger.LogWarning("🚫 SetCameraMode ignorado para {Ip}. TipoEvento inválido='{Tipo}'.", ip, tipoEvento);
            return;
        }

        _cameraModeOverrides[ip] = mode;
        _logger.LogInformation("⚙️ Modo de cámara actualizado [{Ip}] -> {Mode}", ip, mode);
    }

    // Método usado por el Endpoint Telnet/Status
    public Dictionary<string, EstadisticasCamara> ObtenerEstadisticas() 
        => _stats.ToDictionary(k => k.Key, v => v.Value);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔵 TelnetWorkerService Iniciado (Conexiones + Eventos)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Preguntamos al Manager qué cámaras existen en BD
                var camaras = await _cameraManager.ObtenerCamarasBd();

                // Guard-rail: si por algún motivo transitorio la BD devuelve 0 cámaras,
                // NO desconectamos todo (eso genera un bucle de reconexión infinito).
                if (camaras.Count == 0)
                {
                    _logger.LogWarning("⚠️ La BD devolvió 0 cámaras. Manteniendo conexiones actuales y reintentando.");
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                foreach (var cam in camaras)
                {
                    var mode = (cam.TipoEvento ?? string.Empty).Trim().ToLowerInvariant();
                    if (mode != "grid" && mode != "pallet" && mode != "camion")
                    {
                        _logger.LogWarning("🚫 Cámara {Ip}:{Puerto} tiene TipoEvento inválido='{Tipo}'. Se ignorará.", cam.IpCamara, cam.Puerto, cam.TipoEvento);
                        _cameraModeOverrides.TryRemove(cam.IpCamara, out _);
                        continue;
                    }

                    _cameraModeOverrides[cam.IpCamara] = mode;

                    // Inicializar estado de camion si es primera vez
                    if (mode == "camion" && !_camionEstado.ContainsKey(cam.IpCamara))
                    {
                        var estadoInicial = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                        _camionEstado[cam.IpCamara] = estadoInicial;
                        _logger.LogInformation("🟢 [{Ip}] Estado camion inicializado (vacío)", cam.IpCamara);
                    }
                }
                var ipsActivas = camaras.Select(c => c.IpCamara).ToHashSet();

                // 2. Desconectamos las que fueron borradas de la BD
                foreach (var ip in _clients.Keys)
                {
                    if (!ipsActivas.Contains(ip))
                    {
                        _logger.LogWarning("🧹 Desconectando {Ip} porque ya no está registrada en BD.", ip);
                        DesconectarCamara(ip);
                    }
                }

                // 3. Conectamos las nuevas o desconectadas
                foreach (var cam in camaras)
                {
                    // Si no está conectada Y no se está intentando conectar ahora mismo
                    if (!_clients.ContainsKey(cam.IpCamara) && !_conectando.ContainsKey(cam.IpCamara))
                    {
                        _ = ConectarYEscuchar(cam, stoppingToken);
                    }
                }

                await Task.Delay(5000, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error en ciclo Telnet: {msg}", ex.Message);
                await Task.Delay(10000, stoppingToken);
            }
        }
    }

    private async Task ConectarYEscuchar(EstadisticasCamara cam, CancellationToken appToken)
    {
        var ip = cam.IpCamara;
        if (!_conectando.TryAdd(ip, true)) return; // Lock simple

        var cts = CancellationTokenSource.CreateLinkedTokenSource(appToken);
        _cancellationSources[ip] = cts;

        try
        {
            _logger.LogInformation("⏳ Conectando a {Ip}:{Port}...", ip, cam.Puerto);
            var client = new TcpClient();
            
            // Timeout de conexión 5s
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
            
            await client.ConnectAsync(ip, cam.Puerto, linked.Token);

            // Reduce desconexiones espurias en conexiones largas
            try { client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }

            _clients[ip] = client;
            
            // Inicializamos stats
            _stats[ip] = new EstadisticasCamara(ip, cam.Puerto, cam.RutaCarpeta, cam.Nombre, cam.Sucursal, cam.TipoEvento) 
            { 
                EstaConectada = true, 
                UltimoMensaje = "Conectada",
                HoraUltimoMensaje = DateTime.UtcNow 
            };
            
            _logger.LogInformation("🔌 Conectado a {Ip}", ip);

            using var stream = client.GetStream();

            // Muchos simuladores/dispositivos NO garantizan '\n' por mensaje.
            // TCP puede fragmentar/coalescer; por eso hacemos framing tolerante:
            // - separamos por '\n' cuando existe
            // - si vienen varios mensajes concatenados sin '\n', separamos por prefijo "S:"
            // - si no llega nada por un rato, hacemos flush del buffer como un mensaje
            var buffer = new byte[4096];
            var pending = new StringBuilder();
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

            while (!cts.Token.IsCancellationRequested)
            {
                int bytesRead;
                using var readTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                using var linkedRead = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, readTimeout.Token);
                try
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, linkedRead.Token);
                }
                catch (OperationCanceledException) when (readTimeout.IsCancellationRequested && !cts.Token.IsCancellationRequested)
                {
                    // Timeout de lectura: si hay datos pendientes, los procesamos como un mensaje completo.
                    if (pending.Length > 0)
                    {
                        await DrainAndProcessAsync(ip, pending, flushAll: true);
                    }
                    continue;
                }

                if (bytesRead == 0)
                {
                    _logger.LogWarning("⚠️ [{Ip}] Conexión cerrada por remoto (EOF).", ip);
                    break;
                }

                var chunk = utf8.GetString(buffer, 0, bytesRead);
                if (!string.IsNullOrEmpty(chunk))
                {
                    pending.Append(chunk);
                }

                await DrainAndProcessAsync(ip, pending, flushAll: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ Desconexión en {Ip}: {Msg}", ip, ex.Message);
        }
        finally
        {
            DesconectarCamara(ip);
            _conectando.TryRemove(ip, out _);
        }
    }

    private async Task ProcesarMensaje(string ip, string msg)
    {
        // Normalizaciones de framing:
        // - Algunos equipos prefijan con "S:" (snapshot). Lo removemos para no crear un estante falso "S".
        // - Quitamos controles no imprimibles.
        if (!string.IsNullOrWhiteSpace(msg))
        {
            msg = msg.Trim();
            if (msg.StartsWith("S:", StringComparison.OrdinalIgnoreCase))
            {
                msg = msg.Substring(2).Trim();
            }

            msg = new string(msg.Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
        }

        // Actualizar estadísticas visuales
        if (_stats.TryGetValue(ip, out var s))
        {
            s.MensajesRecibidos++;
            s.UltimoMensaje = msg;
            s.HoraUltimoMensaje = DateTime.UtcNow;
        }

        // Modo: lo calculamos ANTES del cooldown para poder ajustar el filtro.
        var mode = _cameraModeOverrides.TryGetValue(ip, out var overrideMode) ? overrideMode : null;
        if (string.IsNullOrWhiteSpace(mode))
        {
            _logger.LogWarning("🚫 [{Ip}] Cámara sin modo configurado. Ignorando mensaje.", ip);
            return;
        }

        if (mode != "grid" && mode != "pallet" && mode != "camion")
        {
            _logger.LogWarning("🚫 [{Ip}] Modo inválido='{Mode}'. Ignorando mensaje.", ip, mode);
            return;
        }

        // --- LÓGICA DE FILTRADO (COOLDOWN) ---
        var ahora = DateTime.UtcNow;
        // En GRID necesitamos procesar duplicados para poder contar lecturas consecutivas.
        // En CAMION también necesitamos contar lecturas consecutivas por sección.
        if (mode != "grid" && mode != "camion")
        {
            var cooldown = TimeSpan.FromSeconds(2);
            if (_ultimoMsg.TryGetValue(ip, out var lastMsg) && lastMsg == msg)
            {
                if (_ultimoTime.TryGetValue(ip, out var lastTime) && (ahora - lastTime) < cooldown)
                    return;
            }
        }
        _ultimoMsg[ip] = msg;
        _ultimoTime[ip] = ahora;

        _logger.LogInformation("📨 [{Ip}] Evento: {Msg}", ip, msg);

        // --- GRID (ESTANTE:... [+ ESTANTE:...]) ---
        string? estanteria = null;
        var cajasActuales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? tipoGridEvent = null;
        var cambios = new List<(string tipo, string estanteria, string caja)>();

        var puertoCamaraForKey = _stats.TryGetValue(ip, out var statsForKey) ? statsForKey.Puerto : 23;
        var cameraKey = $"{ip}:{puertoCamaraForKey}";

        var estantes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var gridCandidate = mode == "grid" && TryParseCompositeRawShelvesFormat(msg, out estantes);

        if (gridCandidate && estantes.Count > 0)
        {
            // Estantes inferidos por cámara: se actualizan dinámicamente según el RAW más reciente.
            // Si cambia el layout (cantidad/nombres), no requiere borrar estado manual: se reemplaza.
            var newLayout = new HashSet<string>(estantes.Keys, StringComparer.OrdinalIgnoreCase);
            var hadLayoutBefore = _gridEstantesDefinidosPorCamara.TryGetValue(cameraKey, out var previousLayout);
            var layoutChanged = false;
            if (hadLayoutBefore)
            {
                if (!previousLayout.SetEquals(newLayout))
                {
                    layoutChanged = true;
                    var removed = previousLayout.Except(newLayout).ToList();
                    var added = newLayout.Except(previousLayout).ToList();
                    _logger.LogWarning(
                        "🧱 [{Cam}] Layout de estantes cambió. +{Added} -{Removed}. Nuevo={Nuevo}",
                        cameraKey,
                        string.Join(", ", added),
                        string.Join(", ", removed),
                        string.Join(", ", newLayout));
                }
            }
            else
            {
                _logger.LogInformation("🧱 [{Cam}] Estantes detectados en primer RAW: {Estantes}", cameraKey, string.Join(", ", newLayout));
            }
            _gridEstantesDefinidosPorCamara[cameraKey] = newLayout;

            var mapasPorEstanteria = _estadoEstantesPorCamara.GetOrAdd(cameraKey, _ => new ConcurrentDictionary<string, HashSet<string>>());
            var pendingByCamera = _gridPendingChanges.GetOrAdd(cameraKey, _ => new ConcurrentDictionary<string, GridPendingChange>(StringComparer.OrdinalIgnoreCase));

            // Prune de estado previo para estantes que ya no existen en el layout actual
            var toRemoveState = mapasPorEstanteria.Keys
                .Where(k => !newLayout.Contains(k))
                .ToList();
            foreach (var k in toRemoveState)
            {
                mapasPorEstanteria.TryRemove(k, out _);

                // Limpiar pendings asociados a estantes que ya no existen
                foreach (var pk in pendingByCamera.Keys.Where(x => x.StartsWith(k + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                    pendingByCamera.TryRemove(pk, out _);
            }

            foreach (var kvp in estantes)
            {
                var nombreEstante = kvp.Key;
                var nuevasCajas = kvp.Value;

                var hadPrev = mapasPorEstanteria.TryGetValue(nombreEstante, out var prevCommitted);

                // Primer snapshot por estante: baseline sin generar altas/bajas (evita spam y falsos al arrancar).
                if (!hadPrev || prevCommitted == null)
                {
                    mapasPorEstanteria[nombreEstante] = new HashSet<string>(nuevasCajas, StringComparer.OrdinalIgnoreCase);

                    // Limpiar pendings de ese estante si existían.
                    foreach (var pk in pendingByCamera.Keys.Where(x => x.StartsWith(nombreEstante + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                        pendingByCamera.TryRemove(pk, out _);

                    continue;
                }

                var updatedCommitted = new HashSet<string>(prevCommitted, StringComparer.OrdinalIgnoreCase);
                ApplyGridStabilityFilter(nombreEstante, updatedCommitted, nuevasCajas, pendingByCamera, cambios);
                mapasPorEstanteria[nombreEstante] = updatedCommitted;
            }

            var anyRemovidas = cambios.Any(c => string.Equals(c.tipo, "grid.baja", StringComparison.OrdinalIgnoreCase));
            var anyAgregadas = cambios.Any(c => string.Equals(c.tipo, "grid.alta", StringComparison.OrdinalIgnoreCase));

            // NO generamos eventos de movimiento/cambio. Sólo alta y baja.
            // Si en un RAW hay altas y bajas, se persisten como eventos separados.
            if (anyRemovidas)
                tipoGridEvent = "grid.baja";
            else if (anyAgregadas)
                tipoGridEvent = "grid.alta";

            // Para persistencia/UI: estantería = lista de estantes del mensaje; cajas = unión
            var estanteriaList = string.Join(',', estantes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
            var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in estantes.Values) union.UnionWith(set);
            estanteria = estanteriaList;
            cajasActuales = union;

            // Para UI: mapping por estante (sin usar '|' porque ya es separador en Descripcion)
            // Formato: estantes=ESTANTE-GPU:CAJA1,CAJA2;ESTANTE-CPU:CAJA3,CAJA4
            var estantesDescripcion = string.Join(';',
                estantes
                    .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kvp => $"{kvp.Key}:{string.Join(',', kvp.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}"));

            if (cambios.Count > 0)
            {
                foreach (var cambio in cambios)
                {
                    _logger.LogInformation("✅ [{Ip}] {Tipo} estanteria={Est} caja={Caja} raw={Raw}", ip, cambio.tipo, cambio.estanteria, cambio.caja, msg);
                }
            }
            else
            {
                _logger.LogInformation("ℹ️ [{Ip}] RAW recibido sin cambios detectados estanteria={Est} tipo={Tipo} raw={Raw}", ip, estanteria, tipoGridEvent ?? "n/a", msg);
            }

            // Si no hubo cambios, igual persistimos un snapshot en el primer RAW (o si cambia el layout)
            // para que el WMS pueda renderizar racks/estantes aunque esté VACIO.
            if (cambios.Count == 0)
            {
                if (!hadLayoutBefore || layoutChanged)
                {
                    tipoGridEvent = "grid.snapshot";
                    await GuardarYPublicarGridAsync(ip, msg, estanteria, cajasActuales, tipoGridEvent, cambios, estantesDescripcion);
                }
                return;
            }

            await GuardarYPublicarGridAsync(ip, msg, estanteria, cajasActuales, tipoGridEvent, cambios, estantesDescripcion);
            return;
        }

        // --- PALLET (VACIO o lista de CAJA-... sin prefijo de estantería) ---
        if (mode == "pallet" && TryParsePalletFormat(msg, out var esVacioPallet, out var cajasPallet))
        {
            await ProcesarPalletAsync(ip, esVacioPallet, cajasPallet);
            return;
        }

        // --- CAMION (secciones reservadas con VACIO o CAMION-XX) ---
        if (mode == "camion" && TryParseCamionFormat(msg, out var seccionesCamion))
        {
            await ProcesarCamionAsync(ip, seccionesCamion, msg);
            return;
        }
    }

    private static void ApplyGridStabilityFilter(
        string estante,
        HashSet<string> committed,
        HashSet<string> observed,
        ConcurrentDictionary<string, GridPendingChange> pending,
        List<(string tipo, string estanteria, string caja)> cambios)
    {
        // Consideramos todas las cajas que importan en este frame.
        var universe = new HashSet<string>(committed, StringComparer.OrdinalIgnoreCase);
        universe.UnionWith(observed);

        foreach (var caja in universe)
        {
            var isPresentCommitted = committed.Contains(caja);
            var isPresentObserved = observed.Contains(caja);
            var key = $"{estante}|{caja}";

            // Si el observado coincide con el estado comprometido, cancelamos cualquier pending.
            if (isPresentObserved == isPresentCommitted)
            {
                pending.TryRemove(key, out _);
                continue;
            }

            var intendedPresent = isPresentObserved;
            var p = pending.AddOrUpdate(
                key,
                _ => new GridPendingChange { IntendedPresent = intendedPresent, Count = 1 },
                (_, existing) =>
                {
                    if (existing.IntendedPresent != intendedPresent)
                    {
                        existing.IntendedPresent = intendedPresent;
                        existing.Count = 1;
                    }
                    else
                    {
                        existing.Count++;
                    }
                    return existing;
                });

            if (p.Count < StabilityThreshold)
                continue;

            // Confirmación alcanzada: aplicamos el cambio y emitimos evento.
            if (intendedPresent)
            {
                committed.Add(caja);
                cambios.Add(("grid.alta", estante, caja));
            }
            else
            {
                committed.Remove(caja);
                cambios.Add(("grid.baja", estante, caja));
            }

            pending.TryRemove(key, out _);
        }
    }

    private async Task DrainAndProcessAsync(string ip, StringBuilder pending, bool flushAll)
    {
        // 1) Separamos por '\n' si existe (formato tradicional)
        while (true)
        {
            var s = pending.ToString();
            var idx = s.IndexOf('\n');
            if (idx < 0) break;

            var line = s.Substring(0, idx).Trim('\r', '\n', ' ', '\t');
            pending.Clear();
            pending.Append(s.Substring(idx + 1));

            if (!string.IsNullOrWhiteSpace(line))
            {
                await SafeProcesarMensajeAsync(ip, line);
            }
        }

        // 2) Si no hay '\n', pero hay múltiples "S:" concatenados, separamos por eso.
        //    Ej: S:...S:...S:...
        while (true)
        {
            var s = pending.ToString();
            var first = IndexOfStartMarker(s, startIndex: 0);
            if (first < 0) break;

            var second = IndexOfStartMarker(s, startIndex: first + 2);
            if (second < 0) break;

            var msg = s.Substring(first, second - first).Trim();
            pending.Clear();
            pending.Append(s.Substring(second));

            if (!string.IsNullOrWhiteSpace(msg))
            {
                await SafeProcesarMensajeAsync(ip, msg);
            }
        }

        // 3) Flush por inactividad: si pedimos flushAll, procesamos lo que quedó.
        if (flushAll)
        {
            var tail = pending.ToString().Trim();
            pending.Clear();
            if (!string.IsNullOrWhiteSpace(tail))
            {
                await SafeProcesarMensajeAsync(ip, tail);
            }
        }
    }

    private static int IndexOfStartMarker(string s, int startIndex)
    {
        if (string.IsNullOrEmpty(s) || startIndex >= s.Length) return -1;
        return s.IndexOf("S:", startIndex, StringComparison.OrdinalIgnoreCase);
    }

    private async Task SafeProcesarMensajeAsync(string ip, string msg)
    {
        try
        {
            var trimmed = msg.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return;
            await ProcesarMensaje(ip, trimmed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error procesando RAW de {Ip}: {Raw}", ip, msg);
        }
    }

    // Nuevo formato RAW: "ESTANTE-NOMBRE:VACIO" o "ESTANTE-NOMBRE:CAJA-6|CAJA-8|..."
    // También soporta compuesto: "ESTANTE-A:...+ESTANTE-B:..." (múltiples estantes en un mensaje)
    private static bool TryParseNewRawShelfFormat(string msg, out string? estanteria, out HashSet<string> cajas)
    {
        estanteria = null;
        cajas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(msg)) return false;

        try
        {
            var idx = msg.IndexOf(':');
            if (idx <= 0) return false;

            var prefix = msg.Substring(0, idx).Trim();
            // Si el prefijo indica pallet, NO es formato de estantería
            if (prefix.StartsWith("PALLET", StringComparison.OrdinalIgnoreCase)) return false;
            estanteria = prefix;

            var rest = msg.Substring(idx + 1).Trim();

            if (string.Equals(rest, "VACIO", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(estanteria);
            }

            var parts = rest.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in parts)
            {
                var token = p.Trim();
                if (token.Length == 0) continue;
                if (token.StartsWith("CAJA-", StringComparison.OrdinalIgnoreCase))
                    token = token.Substring("CAJA-".Length);
                else if (token.StartsWith("CAJA", StringComparison.OrdinalIgnoreCase))
                    token = token.Substring("CAJA".Length).TrimStart('-', ':');

                if (string.IsNullOrWhiteSpace(token)) continue;
                cajas.Add(token);
            }

            return !string.IsNullOrWhiteSpace(estanteria);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseCompositeRawShelvesFormat(string msg, out Dictionary<string, HashSet<string>> estantes)
    {
        estantes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(msg)) return false;

        // Un mensaje puede traer varios estantes concatenados por '+'
        var segments = msg.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return false;

        foreach (var seg in segments)
        {
            if (!TryParseNewRawShelfFormat(seg, out var est, out var cajas))
                continue;
            if (string.IsNullOrWhiteSpace(est))
                continue;

            estantes[est] = cajas;
        }

        return estantes.Count > 0;
    }

    private static bool TryParsePalletFormat(string msg, out bool esVacio, out HashSet<string> cajas)
    {
        esVacio = false;
        cajas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(msg)) return false;
        // Aceptar prefijo opcional "PALLET-...:" y procesar la parte derecha
        var payload = msg;
        var idx = msg.IndexOf(':');
        if (idx >= 0)
        {
            var left = msg.Substring(0, idx).Trim();
            var right = msg.Substring(idx + 1).Trim();
            if (left.StartsWith("PALLET", StringComparison.OrdinalIgnoreCase))
            {
                payload = right;
            }
            else
            {
                return false; // otros prefijos no son pallet
            }
        }

        if (payload.Equals("VACIO", StringComparison.OrdinalIgnoreCase))
        {
            esVacio = true;
            return true;
        }

        var parts = payload.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var token = part.Trim();
            if (token.Length == 0) continue;
            if (token.StartsWith("CAJA-", StringComparison.OrdinalIgnoreCase))
                token = token.Substring("CAJA-".Length);
            else if (token.StartsWith("CAJA", StringComparison.OrdinalIgnoreCase))
                token = token.Substring("CAJA".Length).TrimStart('-', ':');
            else
                continue; // ignorar tokens que no parezcan CAJA

            if (string.IsNullOrWhiteSpace(token)) continue;
            cajas.Add(token);
        }

        return cajas.Count > 0;
    }

    // Formato camion: "seccion1:VACIO|seccion2:CAMION87"
    private static bool TryParseCamionFormat(string msg, out Dictionary<string, string?> secciones)
    {
        secciones = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(msg)) return false;

        var parts = msg.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var idx = part.IndexOf(':');
            if (idx <= 0) continue;
            var seccion = part.Substring(0, idx).Trim();
            var valor = part.Substring(idx + 1).Trim();
            if (string.IsNullOrWhiteSpace(seccion)) continue;
            if (valor.Equals("VACIO", StringComparison.OrdinalIgnoreCase))
            {
                secciones[seccion] = null;
            }
            else
            {
                secciones[seccion] = valor;
            }
        }

        return secciones.Count > 0;
    }

    private async Task GuardarYPublicarGridAsync(string ip, string msg, string? estanteria, HashSet<string> cajasActuales, string? tipoGridEvent, List<(string tipo, string estanteria, string caja)> cambios, string? estantesDescripcion)
    {
        using var scope = _scopeFactory.CreateScope();
        var eventStorageService = scope.ServiceProvider.GetRequiredService<IEventStorageService>();

        try
        {
            var puertoCamara = _stats.TryGetValue(ip, out var statsCamara) ? statsCamara.Puerto : 23;

            // Persistimos como máximo 2 eventos: uno de ALTAS y uno de BAJAS.
            var hasAltas = cambios.Any(c => string.Equals(c.tipo, "grid.alta", StringComparison.OrdinalIgnoreCase));
            var hasBajas = cambios.Any(c => string.Equals(c.tipo, "grid.baja", StringComparison.OrdinalIgnoreCase));

            var latestFramePath = _cameraManager.GetLatestFile(ip);
            string rutaCarpeta;
            int? fromFrame = null;
            int? toFrame = null;
            if (!string.IsNullOrEmpty(latestFramePath))
            {
                rutaCarpeta = Path.GetDirectoryName(latestFramePath) ?? string.Empty;
                var fileName = Path.GetFileNameWithoutExtension(latestFramePath);
                var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d+)$");
                if (match.Success && int.TryParse(match.Value, out int frameNum))
                {
                    fromFrame = Math.Max(0, frameNum - _config.FramesAdyacentesTelnetAntes);
                    toFrame = frameNum + _config.FramesAdyacentesTelnetDespues;
                }
            }
            else
            {
                _logger.LogWarning("No se encontró el último frame para la cámara {Ip}.", ip);
                rutaCarpeta = _cameraManager.ObtenerRutaCarpeta(ip) ?? string.Empty;
            }

            async Task SaveAndEmitAsync(string gridType)
            {
                var evento = new AltaEventoModel
                {
                    Nombre = $"GRID {gridType.ToUpperInvariant()} - {estanteria}",
                    IpCamara = ip,
                    Puerto = puertoCamara,
                    EsEventoGuardado = true,
                    FechaEvento = DateTime.UtcNow,
                    TipoEvento = "grid",
                    FramePath = latestFramePath,
                    RutaCarpeta = rutaCarpeta,
                    FromFrame = fromFrame,
                    ToFrame = toFrame,
                    // Mantener snapshot actual para UI (estantes + cajas actuales)
                    Descripcion = $"tipo=grid | {gridType} | estanteria={estanteria} | cajas={string.Join(',', cajasActuales)} | estantes={estantesDescripcion}"
                };

                await eventStorageService.SaveEventAsync(evento);
                await EmitSavedEventAddedAsync(evento);
            }

            if (hasAltas)
            {
                await SaveAndEmitAsync("grid.alta");
            }
            if (hasBajas)
            {
                await SaveAndEmitAsync("grid.baja");
            }

            // Snapshot: usado para bootstrap de layout (primer RAW o cambio de layout) sin generar alta/baja.
            if (!hasAltas && !hasBajas && string.Equals(tipoGridEvent, "grid.snapshot", StringComparison.OrdinalIgnoreCase))
            {
                await SaveAndEmitAsync("grid.snapshot");
            }

            // Publicación a RabbitMQ por caja afectada
            var queueName = ip.Replace('.', '_');
            var uri = new Uri($"queue:{queueName}");
            var endpoint = await _bus.GetSendEndpoint(uri);

            foreach (var cambio in cambios)
            {
                await endpoint.Send(new Shared.Contracts.Models.EventoMovimientoDetectado
                {
                    IpCamara = ip,
                    MensajeCrudoEvento = msg,
                    Momento = DateTime.UtcNow,
                    Estanteria = cambio.estanteria,
                    CajaQr = cambio.caja,
                    TipoGridEvent = cambio.tipo
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error procesando evento Telnet para {Ip}: {Msg}", ip, ex.Message);
        }
    }

    private async Task EmitSavedEventAddedAsync(AltaEventoModel evento)
    {
        try
        {
            _logger.LogInformation("📡 [TelnetWorker] Emitiendo SavedEventAdded para evento ID={Id}, Nombre={Nombre}", evento.Id, evento.Nombre);
            await _hub.Clients.Group(EventsHubGroups.Camera(evento.IpCamara)).SendAsync("SavedEventAdded", evento);
            _logger.LogInformation("✅ [TelnetWorker] SavedEventAdded emitido correctamente");
        }
        catch (Exception exHub)
        {
            _logger.LogError(exHub, "❌ [TelnetWorker] Error emitiendo al hub SignalR");
        }
    }

    private async Task EmitPalletEventAddedAsync(PalletEventModel evento)
    {
        try
        {
            _logger.LogInformation("📡 [TelnetWorker] Emitiendo PalletEventAdded para evento ID={Id}, Ip={Ip}", evento.Id, evento.IpCamara);
            await _hub.Clients.Group(EventsHubGroups.Camera(evento.IpCamara)).SendAsync("PalletEventAdded", evento);
        }
        catch (Exception exHub)
        {
            _logger.LogError(exHub, "❌ [TelnetWorker] Error emitiendo PalletEventAdded");
        }
    }

    private async Task EmitCamionEventAddedAsync(CamionEventModel evento)
    {
        try
        {
            _logger.LogInformation("📡 [TelnetWorker] Emitiendo CamionEventAdded para evento ID={Id}, Ip={Ip}", evento.Id, evento.IpCamara);
            await _hub.Clients.Group(EventsHubGroups.Camera(evento.IpCamara)).SendAsync("CamionEventAdded", evento);
        }
        catch (Exception exHub)
        {
            _logger.LogError(exHub, "❌ [TelnetWorker] Error emitiendo CamionEventAdded");
        }
    }

    private async Task ProcesarPalletAsync(string ip, bool esVacio, HashSet<string> cajas)
    {
        var activo = _palletActivo.TryGetValue(ip, out var flag) && flag;

        if (esVacio)
        {
            _palletActivo[ip] = false;
            _palletUltimasCajas[ip] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _logger.LogInformation("ℹ️ [{Ip}] PALLET vacío / standby", ip);
            return;
        }

        if (cajas.Count == 0)
        {
            _logger.LogInformation("ℹ️ [{Ip}] RAW pallet sin cajas válidas: {Raw}", ip, string.Join('|', cajas));
            return;
        }

        if (!activo)
        {
            var palletId = _palletCounters.AddOrUpdate(ip, 1, (_, current) => current + 1);
            _palletActivo[ip] = true;
            _palletUltimasCajas[ip] = new HashSet<string>(cajas, StringComparer.OrdinalIgnoreCase);

            await RegistrarPalletAsync(ip, palletId, cajas);
        }
        else
        {
            // Ya hay un pallet en curso; si las cajas cambiaron radicalmente podríamos registrar otro, pero por ahora sólo se marca activo.
            _palletUltimasCajas[ip] = new HashSet<string>(cajas, StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task RegistrarPalletAsync(string ip, int palletId, IEnumerable<string> cajas)
    {
        using var scope = _scopeFactory.CreateScope();
        var eventStorageService = scope.ServiceProvider.GetRequiredService<IEventStorageService>();

        try
        {
            var puertoCamara = _stats.TryGetValue(ip, out var statsCamara) ? statsCamara.Puerto : 23;
            var listaCajas = string.Join('|', cajas);

            var evento = new Shared.Contracts.Models.PalletEventModel
            {
                PalletId = palletId,
                IpCamara = ip,
                Puerto = puertoCamara,
                FechaEvento = DateTime.UtcNow,
                Cajas = listaCajas
            };

            EventFrameRange? frameRange = null;
            var latestFramePath = _cameraManager.GetLatestFile(ip);
            if (!string.IsNullOrEmpty(latestFramePath))
            {
                await Task.Delay(0);
                frameRange = _cameraManager.GetFrameRangeForEvent(latestFramePath, _config.FramesAdyacentesTelnetAntes, _config.FramesAdyacentesTelnetDespues);
                if (frameRange != null)
                {
                    evento.FramePath = latestFramePath;
                    evento.FromFrame = frameRange.FromFrame;
                    evento.ToFrame = frameRange.ToFrame;
                    evento.RutaCarpeta = frameRange.FolderPath;
                }
                else
                {
                    evento.FramePath = latestFramePath;
                }
            }
            evento.RutaCarpeta = frameRange?.FolderPath ?? _cameraManager.ObtenerRutaCarpeta(ip) ?? string.Empty;

            await eventStorageService.SavePalletEventAsync(evento);

            await EmitPalletEventAddedAsync(evento);

            _logger.LogInformation("✅ [{Ip}] PALLET #{PalletId} registrado con {Count} cajas: {Cajas}", ip, palletId, cajas.Count(), listaCajas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registrando pallet para {Ip}", ip);
        }
    }

    private async Task ProcesarCamionAsync(string ip, Dictionary<string, string?> secciones, string raw)
    {
        var mapa = _camionEstado.GetOrAdd(ip, _ => new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
        var pendingByCamera = _camionPendingChanges.GetOrAdd(ip, _ => new ConcurrentDictionary<string, CamionPendingChange>(StringComparer.OrdinalIgnoreCase));
        var puertoCamara = _stats.TryGetValue(ip, out var statsCamara) ? statsCamara.Puerto : 23;

        var eventos = new List<Shared.Contracts.Models.CamionEventModel>();

        _logger.LogInformation("🔍 [{Ip}] ProcesarCamionAsync iniciado. Secciones recibidas: {Secciones}. Estado anterior: {Estado}", 
            ip, string.Join(", ", secciones.Select(s => $"{s.Key}={s.Value ?? "null"}")), 
            string.Join(", ", mapa.Select(s => $"{s.Key}={s.Value ?? "null"}")));

        // Si es la primera vez que recibimos un raw de camion (mapa vacío), tomamos baseline sin generar eventos.
        var primeraMensaje = mapa.Count == 0;

        foreach (var kvp in secciones)
        {
            var seccion = kvp.Key;
            if (string.IsNullOrWhiteSpace(seccion))
            {
                _logger.LogWarning("    -> Ignorando sección vacía en raw: {Raw}", raw);
                continue;
            }

            var nuevoCamion = kvp.Value; // null si VACIO

            mapa.TryGetValue(seccion, out var anteriorCamion);

            _logger.LogInformation("  [{Ip}] Seccion={Sec} anterior={Ant} nuevo={Nuev} primera={Primera}", ip, seccion, anteriorCamion ?? "null", nuevoCamion ?? "null", primeraMensaje);

            // Primer mensaje: baseline (no genera eventos, ni pendings)
            if (primeraMensaje)
            {
                mapa[seccion] = nuevoCamion;
                pendingByCamera.TryRemove(seccion, out _);
                continue;
            }

            // Si el observado coincide con el estado confirmado: cancelamos pending.
            if (string.Equals(anteriorCamion, nuevoCamion, StringComparison.OrdinalIgnoreCase))
            {
                pendingByCamera.TryRemove(seccion, out _);
                _logger.LogInformation("    -> Sin cambios (estable), pending limpiado");
                continue;
            }

            // Hay diferencia vs estado confirmado: acumulamos lecturas consecutivas.
            var intended = string.IsNullOrEmpty(nuevoCamion) ? null : nuevoCamion;
            var p = pendingByCamera.AddOrUpdate(
                seccion,
                _ => new CamionPendingChange { IntendedCamionId = intended, Count = 1 },
                (_, existing) =>
                {
                    if (!string.Equals(existing.IntendedCamionId, intended, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.IntendedCamionId = intended;
                        existing.Count = 1;
                    }
                    else
                    {
                        existing.Count++;
                    }
                    return existing;
                });

            _logger.LogInformation("    -> Pending cambio: intended={Intended} count={Count}/{Threshold}", p.IntendedCamionId ?? "null", p.Count, StabilityThreshold);

            if (p.Count < StabilityThreshold)
                continue;

            // Confirmado: generamos evento(s) equivalente a la transición anterior -> intended
            if (!string.IsNullOrEmpty(anteriorCamion) && string.IsNullOrEmpty(p.IntendedCamionId))
            {
                eventos.Add(new Shared.Contracts.Models.CamionEventModel
                {
                    IpCamara = ip,
                    Puerto = puertoCamara,
                    Seccion = seccion,
                    CamionId = anteriorCamion ?? string.Empty,
                    TipoEvento = "camion.sefue",
                    Ocupado = false,
                    FechaEvento = DateTime.UtcNow,
                    Raw = raw
                });
                _logger.LogInformation("    -> [CONFIRMADO] Se fue camión {Camion}", anteriorCamion);
            }
            else if (string.IsNullOrEmpty(anteriorCamion) && !string.IsNullOrEmpty(p.IntendedCamionId))
            {
                eventos.Add(new Shared.Contracts.Models.CamionEventModel
                {
                    IpCamara = ip,
                    Puerto = puertoCamara,
                    Seccion = seccion,
                    CamionId = p.IntendedCamionId ?? string.Empty,
                    TipoEvento = "camion.llego",
                    Ocupado = true,
                    FechaEvento = DateTime.UtcNow,
                    Raw = raw
                });
                _logger.LogInformation("    -> [CONFIRMADO] Llegó camión {Camion}", p.IntendedCamionId);
            }
            else if (!string.IsNullOrEmpty(anteriorCamion) && !string.IsNullOrEmpty(p.IntendedCamionId) && !string.Equals(anteriorCamion, p.IntendedCamionId, StringComparison.OrdinalIgnoreCase))
            {
                eventos.Add(new Shared.Contracts.Models.CamionEventModel
                {
                    IpCamara = ip,
                    Puerto = puertoCamara,
                    Seccion = seccion,
                    CamionId = anteriorCamion ?? string.Empty,
                    TipoEvento = "camion.sefue",
                    Ocupado = false,
                    FechaEvento = DateTime.UtcNow,
                    Raw = raw
                });
                eventos.Add(new Shared.Contracts.Models.CamionEventModel
                {
                    IpCamara = ip,
                    Puerto = puertoCamara,
                    Seccion = seccion,
                    CamionId = p.IntendedCamionId ?? string.Empty,
                    TipoEvento = "camion.llego",
                    Ocupado = true,
                    FechaEvento = DateTime.UtcNow,
                    Raw = raw
                });
                _logger.LogInformation("    -> [CONFIRMADO] Reemplazo: {AntCamion} -> {NuevoCamion}", anteriorCamion, p.IntendedCamionId);
            }

            // Actualizamos estado confirmado y limpiamos pending
            mapa[seccion] = p.IntendedCamionId;
            pendingByCamera.TryRemove(seccion, out _);
        }

        if (eventos.Count == 0)
        {
            _logger.LogInformation("ℹ️ [{Ip}] RAW camion sin cambios", ip);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IEventStorageService>();

        _logger.LogInformation("💾 [{Ip}] Guardando {Count} eventos de camion", ip, eventos.Count);
        foreach (var ev in eventos)
        {
            await storage.SaveCamionEventAsync(ev);
            _logger.LogInformation("✅ [{Ip}] {Tipo} seccion={Seccion} camion={Camion} ocupado={Ocupado} raw={Raw}", ip, ev.TipoEvento, ev.Seccion, ev.CamionId, ev.Ocupado, raw);

            await EmitCamionEventAddedAsync(ev);
        }
    }

    private static bool TryParseShelfAndQr(string msg, out string? estanteria, out string? qr, out bool vacio)
    {
        estanteria = null;
        qr = null;
        vacio = false;

        if (string.IsNullOrWhiteSpace(msg)) return false;

        try
        {
            // Ejemplos esperados:
            // "Estanteria:GPUS|Caja:SKSIASJ"
            // "Estanteria:GPUS|VACIO"
            // Admite separador '|' y ';'
            var parts = msg.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in parts)
            {
                var seg = p.Trim();
                if (seg.StartsWith("Estanteria:", StringComparison.OrdinalIgnoreCase))
                {
                    estanteria = seg.Substring("Estanteria:".Length).Trim();
                }
                else if (seg.StartsWith("Caja:", StringComparison.OrdinalIgnoreCase))
                {
                    qr = seg.Substring("Caja:".Length).Trim();
                }
                else if (seg.Equals("VACIO", StringComparison.OrdinalIgnoreCase))
                {
                    vacio = true;
                }
            }

            // Normalización simple
            if (string.IsNullOrWhiteSpace(qr) && vacio == false)
            {
                // Revisar si está codificado como QR:XXXX
                var qrAlt = parts.FirstOrDefault(x => x.Trim().StartsWith("QR:", StringComparison.OrdinalIgnoreCase));
                if (qrAlt != null)
                {
                    qr = qrAlt.Split(':', 2)[1].Trim();
                }
            }

            return !string.IsNullOrWhiteSpace(estanteria);
        }
        catch
        {
            return false;
        }
    }

    public void DesconectarCamara(string ip)
    {
        if (_clients.TryRemove(ip, out var client)) 
            try { client.Close(); } catch { }
        
        if (_cancellationSources.TryRemove(ip, out var cts)) 
            cts.Cancel();

        if (_stats.TryGetValue(ip, out var s))
        {
            s.EstaConectada = false;
            s.UltimoMensaje = "Desconectada";
        }
    }
}
