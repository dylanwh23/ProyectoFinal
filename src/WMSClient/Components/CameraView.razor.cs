using Microsoft.AspNetCore.Components;
using Shared.Contracts.Models;
using WMSClient.Services;
using System.Text.Json;
using Microsoft.JSInterop;

namespace WMSClient.Components;

public partial class CameraView : IAsyncDisposable
{
    [Parameter]
    public AltaEventoModel? SelectedCamera { get; set; }

    [Parameter]
    public EventCallback<AltaEventoModel> OnRequestGoLive { get; set; }

    [Inject]
    public IWmsCameraService CameraService { get; set; } = default!;

    [Inject]
    public HttpClient Http { get; set; } = default!;

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    private const string API_BASE = "http://localhost:5000";
    private const int HISTORY_BUFFER_SIZE = 600;
    private const int SIGNAL_TIMEOUT_SECONDS = 10;

    // Estado
    private string _currentIp = string.Empty;
    private AltaEventoModel? _previousCamera;
    private bool _isEventoGuardado = false;

    private bool IsLive { get; set; } = true;
    private bool IsLoading { get; set; } = false;
    private bool ShowNoSignal { get; set; } = false;
    private string StatusMessage { get; set; } = "";

    // Imágenes y Buffer
    private string ImageSource { get; set; } = string.Empty;
    private List<string> FramesBuffer { get; set; } = new();

    // Slider
    private int SliderValue { get; set; } = 100;
    private int SliderMax { get; set; } = 100;
    private string LabelStart { get; set; } = "";
    private string LabelCurrent { get; set; } = "Tiempo Real";

    // Eventos
    private List<AltaEventoModel> Events { get; set; } = new();

    // Timers
    private System.Timers.Timer? _watchdogTimer;
    private System.Timers.Timer? _loopTimer;

    private int _loadVersion;
    private bool _disposed;

    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    protected override void OnInitialized()
    {
        _watchdogTimer = new System.Timers.Timer(2000);
        _watchdogTimer.Elapsed += async (sender, e) => await CheckHealth();
        _watchdogTimer.AutoReset = true;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (SelectedCamera == null)
        {
            _previousCamera = null;
            _currentIp = string.Empty;
            _isEventoGuardado = false;
            IsLive = true;
            IsLoading = false;
            ShowNoSignal = false;
            FramesBuffer.Clear();
            Events.Clear();
            _watchdogTimer?.Stop();
            _loopTimer?.Stop();
            StatusMessage = "Selecciona una cámara";
            await InvokeAsync(StateHasChanged);
            return;
        }

        var (fromFrame, toFrame) = GetFrameRange(SelectedCamera);
        bool esEvento = fromFrame.HasValue && toFrame.HasValue;

        bool eventoDistinto = _previousCamera == null
            || !string.Equals(SelectedCamera.IpCamara, _previousCamera.IpCamara, StringComparison.OrdinalIgnoreCase)
            || SelectedCamera.Id != _previousCamera.Id
            || GetFrameRange(SelectedCamera) != GetFrameRange(_previousCamera);

        if (eventoDistinto || esEvento != _isEventoGuardado)
        {
            // SOLO cuando realmente vamos a cargar algo nuevo invalidamos lo anterior
            var myLoad = Interlocked.Increment(ref _loadVersion);

            _loopTimer?.Stop();
            _watchdogTimer?.Stop();

            _previousCamera = CloneCamera(SelectedCamera);
            _currentIp = SelectedCamera.IpCamara;
            _isEventoGuardado = esEvento;

            StatusMessage = "Cargando...";
            await InvokeAsync(StateHasChanged);

            if (esEvento)
            {
                await PlayEventoGuardado(myLoad);
            }
            else
            {
                await LoadEvents();
                await GoLiveInternal(myLoad);
            }
        }
    }

    private async Task LoadEvents()
    {
        try
        {
            Events = (await CameraService.GetEventsForCameraAsync(_currentIp))
                .OrderByDescending(e => e.FechaEvento).ToList();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar eventos: {ex.Message}";
        }
    }

    private async Task RequestGoLive()
    {
        if (SelectedCamera == null) return;

        var live = new AltaEventoModel
        {
            Id = SelectedCamera.Id,
            Nombre = SelectedCamera.Nombre,
            Sucursal = SelectedCamera.Sucursal,
            TipoEvento = SelectedCamera.TipoEvento,
            IpCamara = SelectedCamera.IpCamara,
            Puerto = SelectedCamera.Puerto,
            RutaCarpeta = SelectedCamera.RutaCarpeta,
            EstaConectada = SelectedCamera.EstaConectada,
            EsEventoGuardado = false,
            FromFrame = null,
            ToFrame = null,
            FrameInicio = null,
            FrameFin = null,
            Descripcion = null,
            FramePath = null
        };

        if (OnRequestGoLive.HasDelegate)
        {
            await OnRequestGoLive.InvokeAsync(live);
            return;
        }

        // Fallback: si el padre no maneja el cambio, al menos volvemos a vivo localmente.
        var myLoad = Interlocked.Increment(ref _loadVersion);
        await GoLiveInternal(myLoad);
    }

    private async Task GoLiveInternal(int myLoad)
    {
        if (_disposed || myLoad != _loadVersion) return;
        IsLive = true;
        IsLoading = false;
        ShowNoSignal = false;
        FramesBuffer.Clear();
        _loopTimer?.Stop();
        _isEventoGuardado = false;

        SliderMax = 100;
        SliderValue = 100;
        LabelCurrent = "Tiempo Real";
        LabelStart = "";

        UpdateStreamUrl();
        _watchdogTimer?.Start();
        StatusMessage = "Stream en vivo";
        await InvokeAsync(StateHasChanged);
    }

    private async Task GoToBeginningHistory()
    {
        if (!IsLive && FramesBuffer.Count > 0)
        {
            SliderValue = 0;
            ShowFrame(0);
            LabelCurrent = "Frame 1 / " + FramesBuffer.Count;
            _loopTimer?.Stop();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ToggleFullscreen()
    {
        try
        {
            await JS.InvokeVoidAsync("fullscreenHelper.toggleFullscreen", "viewer-layout");
        }
        catch { }
    }

    private async Task GoPause()
    {
        IsLive = false;
        _watchdogTimer?.Stop();
        _loopTimer?.Stop();
        ShowNoSignal = false;
        IsLoading = true;
        ImageSource = "";

        StatusMessage = "Cargando buffer de historial...";
        await InvokeAsync(StateHasChanged);

        await LoadHistoryBuffer();

        IsLoading = false;
        StatusMessage = "Reproduciendo historial";
        await InvokeAsync(StateHasChanged);
    }

    private void TogglePlayPause()
    {
        if (_isEventoGuardado) return;
        if (IsLive)
            _ = GoPause();
        else
            _ = RequestGoLive();
    }

    private async Task LoadHistoryBuffer()
    {
        try
        {
            FramesBuffer.Clear();
            var snapshot = await Http.GetFromJsonAsync<HistorySnapshotDto>(
                $"{API_BASE}/api/buffer/{_currentIp}?count={HISTORY_BUFFER_SIZE}",
                _jsonOptions);

            if (snapshot != null && snapshot.Files.Count > 0)
            {
                FramesBuffer = snapshot.Files;
                SliderMax = FramesBuffer.Count - 1;
                SliderValue = FramesBuffer.Count - 1;
                LabelStart = $"-{FramesBuffer.Count} frames";
                ShowFrame(SliderValue);
            }
            else
            {
                LabelCurrent = "Buffer vacío";
            }
        }
        catch (Exception ex)
        {
            LabelCurrent = "Error historial";
            StatusMessage = $"Error al cargar buffer: {ex.Message}";
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task PlayEventoGuardado(int myLoad)
    {
        if (_disposed || myLoad != _loadVersion) return;
        if (SelectedCamera == null)
        {
            StatusMessage = "No hay evento seleccionado";
            await InvokeAsync(StateHasChanged);
            return;
        }

        // Usar FromFrame/ToFrame si están disponibles, si no, usar FrameInicio/FrameFin
        int? fromFrame = SelectedCamera.FromFrame ?? SelectedCamera.FrameInicio;
        int? toFrame = SelectedCamera.ToFrame ?? SelectedCamera.FrameFin;

        if (!fromFrame.HasValue || !toFrame.HasValue)
        {
            StatusMessage = $"Error: Evento sin frames. FromFrame={SelectedCamera.FromFrame}, FrameInicio={SelectedCamera.FrameInicio}, ToFrame={SelectedCamera.ToFrame}, FrameFin={SelectedCamera.FrameFin}";
            await InvokeAsync(StateHasChanged);
            return;
        }

        IsLive = false;
        _watchdogTimer?.Stop();
        _loopTimer?.Stop();
        ShowNoSignal = false;
        IsLoading = true;
        FramesBuffer.Clear();
        StatusMessage = $"Cargando evento '{SelectedCamera.Nombre}'...";
        await InvokeAsync(StateHasChanged);

        try
        {
            var url = $"{API_BASE}/api/range/{_currentIp}?from={fromFrame.Value}&to={toFrame.Value}";
            var snapshot = await Http.GetFromJsonAsync<HistorySnapshotDto>(url, _jsonOptions);

            if (_disposed || myLoad != _loadVersion) return;

            if (snapshot != null && snapshot.Files.Count > 0)
            {
                FramesBuffer = snapshot.Files;
                SliderMax = FramesBuffer.Count - 1;
                SliderValue = 0;
                LabelStart = "Evento Guardado";
                LabelCurrent = $"{FramesBuffer.Count} frames";

                StartLoopPlayback(myLoad);
                StatusMessage = $"Evento '{SelectedCamera.Nombre}' cargado ({FramesBuffer.Count} frames)";
            }
            else
            {
                LabelCurrent = "Evento sin frames";
                StatusMessage = $"El evento no tiene frames disponibles (rango: {fromFrame.Value}-{toFrame.Value})";
            }
        }
        catch (Exception ex)
        {
            LabelCurrent = "Error evento";
            StatusMessage = $"Error al cargar evento: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void StartLoopPlayback(int myLoad)
    {
        if (FramesBuffer.Count == 0) return;

        int currentIndex = 0;
        _loopTimer?.Stop();
        _loopTimer = new System.Timers.Timer(250); // 4 FPS

        _loopTimer.Elapsed += async (sender, e) =>
        {
            try
            {
                if (_disposed || myLoad != _loadVersion) return;
                if (FramesBuffer.Count == 0) return;
                ShowFrame(currentIndex);
                await InvokeAsync(() =>
                {
                    SliderValue = currentIndex;
                    StateHasChanged();
                });
                currentIndex = (currentIndex + 1) % FramesBuffer.Count;
            }
            catch
            {
                // Si algo se descontrola (dispose/race), frenamos el timer para no romper el circuito.
                try { _loopTimer?.Stop(); } catch { }
            }
        };

        _loopTimer.AutoReset = true;
        _loopTimer.Start();
    }

    private async Task OnSliderChange(ChangeEventArgs e)
    {
        if (IsLive || _isEventoGuardado) return;
        if (int.TryParse(e.Value?.ToString(), out int val))
        {
            SliderValue = val;
            ShowFrame(val);
            _loopTimer?.Stop();
            
            // Actualizar el CSS variable para que el gradiente funcione
            var percent = SliderMax > 0 ? (SliderValue * 100.0 / SliderMax) : 0;
            await InvokeAsync(() =>
            {
                StateHasChanged();
            });
        }
    }

    private void ShowFrame(int index)
    {
        if (index < 0 || index >= FramesBuffer.Count) return;

        var filePath = FramesBuffer[index];
        var encodedPath = Uri.EscapeDataString(filePath);
        ImageSource = $"{API_BASE}/api/frame/{_currentIp}?file={encodedPath}";

        if (!_isEventoGuardado)
            LabelCurrent = $"Frame -{FramesBuffer.Count - index}";
    }

    private void UpdateStreamUrl()
    {
        ImageSource = $"{API_BASE}/api/stream/{_currentIp}?t={DateTime.Now.Ticks}";
    }

    private static (int? from, int? to) GetFrameRange(AltaEventoModel? cam)
    {
        if (cam == null) return (null, null);
        return (cam.FromFrame ?? cam.FrameInicio, cam.ToFrame ?? cam.FrameFin);
    }

    private static AltaEventoModel CloneCamera(AltaEventoModel cam)
    {
        var (from, to) = GetFrameRange(cam);
        return new AltaEventoModel
        {
            Id = cam.Id,
            Nombre = cam.Nombre,
            IpCamara = cam.IpCamara,
            Puerto = cam.Puerto,
            RutaCarpeta = cam.RutaCarpeta,
            EsEventoGuardado = cam.EsEventoGuardado,
            FrameInicio = cam.FrameInicio,
            FrameFin = cam.FrameFin,
            FromFrame = from,
            ToFrame = to,
            FechaEvento = cam.FechaEvento,
            Descripcion = cam.Descripcion,
            EstaConectada = cam.EstaConectada,
            FramePath = cam.FramePath,
            TipoEvento = cam.TipoEvento
        };
    }

    private async Task RefreshStream()
    {
        if (SelectedCamera != null)
        {
            IsLoading = true;
            UpdateStreamUrl();
            StatusMessage = "Reintentando conexión...";
            await InvokeAsync(StateHasChanged);
            
            await Task.Delay(100);
            IsLoading = false;
            StatusMessage = "Stream en vivo";
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RefreshEvents()
    {
        await LoadEvents();
    }

    private async Task OnSelectEvent(AltaEventoModel ev)
    {
        // Usar FromFrame/ToFrame si están disponibles, si no, usar FrameInicio/FrameFin
        int? fromFrame = ev.FromFrame ?? ev.FrameInicio;
        int? toFrame = ev.ToFrame ?? ev.FrameFin;

        if (fromFrame.HasValue && toFrame.HasValue)
        {
            // Copiar los valores al SelectedCamera para que OnParametersSetAsync los procese
            SelectedCamera = new AltaEventoModel
            {
                Id = ev.Id,
                Nombre = ev.Nombre,
                IpCamara = ev.IpCamara,
                Puerto = ev.Puerto,
                RutaCarpeta = ev.RutaCarpeta,
                EsEventoGuardado = true,
                FrameInicio = fromFrame.Value,
                FrameFin = toFrame.Value,
                FechaEvento = ev.FechaEvento,
                Descripcion = ev.Descripcion,
                FromFrame = fromFrame.Value,
                ToFrame = toFrame.Value,
                EstaConectada = ev.EstaConectada,
                FramePath = ev.FramePath
            };

            _previousCamera = null; // forzar recarga en OnParametersSetAsync
            await OnParametersSetAsync();
        }
        else
        {
            StatusMessage = $"Evento '{ev.Nombre}' sin información de frames (FromFrame={ev.FromFrame}, FrameInicio={ev.FrameInicio})";
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task CheckHealth()
    {
        if (!IsLive || string.IsNullOrEmpty(_currentIp) || _isEventoGuardado) return;

        try
        {
            // Usar el endpoint health que retorna cuántos segundos hace que NO hay imagen
            var health = await Http.GetFromJsonAsync<HealthDto>(
                $"{API_BASE}/api/camaras/health/{_currentIp}",
                _jsonOptions);

            bool shouldShowSignal = health != null && health.SecondsAgo > SIGNAL_TIMEOUT_SECONDS;

            if (ShowNoSignal != shouldShowSignal)
            {
                ShowNoSignal = shouldShowSignal;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch
        {
            if (!ShowNoSignal)
            {
                ShowNoSignal = true;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private void HandleImageError()
    {
        // Cuando falla una imagen en vivo, reintentar después de 3 segundos
        if (IsLive && !_isEventoGuardado)
        {
            Task.Delay(3000).ContinueWith(_ =>
            {
                UpdateStreamUrl();
                InvokeAsync(StateHasChanged);
            });
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _disposed = true;
        _watchdogTimer?.Stop();
        _watchdogTimer?.Dispose();
        _loopTimer?.Stop();
        _loopTimer?.Dispose();
    }

    // DTOs
    private class HistorySnapshotDto
    {
        public string HistoryId { get; set; } = "";
        public List<string> Files { get; set; } = new();
    }

    private class HealthDto
    {
        public double SecondsAgo { get; set; }
    }
}
