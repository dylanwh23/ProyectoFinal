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

        bool cameraCambio = SelectedCamera != _previousCamera;
        // Detectar si es evento guardado usando CUALQUIERA de los dos pares disponibles
        bool esEvento = (SelectedCamera.FromFrame.HasValue && SelectedCamera.ToFrame.HasValue) ||
                        (SelectedCamera.FrameInicio.HasValue && SelectedCamera.FrameFin.HasValue);

        if (cameraCambio || esEvento != _isEventoGuardado)
        {
            _previousCamera = SelectedCamera;
            _currentIp = SelectedCamera.IpCamara;
            _isEventoGuardado = esEvento;

            StatusMessage = "Cargando...";
            await InvokeAsync(StateHasChanged);

            if (esEvento)
            {
                await PlayEventoGuardado();
            }
            else
            {
                await LoadEvents();
                await GoLive();
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

    private async Task GoLive()
    {
        IsLive = true;
        IsLoading = false;
        ShowNoSignal = false;
        FramesBuffer.Clear();
        _loopTimer?.Stop();

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
            _ = GoLive();
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

    private async Task PlayEventoGuardado()
    {
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

            if (snapshot != null && snapshot.Files.Count > 0)
            {
                FramesBuffer = snapshot.Files;
                SliderMax = FramesBuffer.Count - 1;
                SliderValue = 0;
                LabelStart = "Evento Guardado";
                LabelCurrent = $"{FramesBuffer.Count} frames";

                StartLoopPlayback();
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

    private void StartLoopPlayback()
    {
        if (FramesBuffer.Count == 0) return;

        int currentIndex = 0;
        _loopTimer?.Stop();
        _loopTimer = new System.Timers.Timer(250); // 4 FPS

        _loopTimer.Elapsed += async (sender, e) =>
        {
            if (FramesBuffer.Count == 0) return;
            ShowFrame(currentIndex);
            await InvokeAsync(() =>
            {
                SliderValue = currentIndex;
                StateHasChanged();
            });
            currentIndex = (currentIndex + 1) % FramesBuffer.Count;
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
