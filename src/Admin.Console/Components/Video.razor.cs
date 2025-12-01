using Microsoft.AspNetCore.Components;
using Shared.Contracts.Models;
using System.Text.Json; // Necesario para opciones JSON

namespace Admin.Console.Components
{
    public partial class Video : IDisposable
    {
        [Inject]
        public HttpClient Http { get; set; } = default!;

        [Parameter]
        public AltaEventoModel? Evento { get; set; }

        [Parameter]
        public EventCallback<AltaEventoModel> OnGuardarEvento { get; set; }

        // ============================================================
        // CONFIGURACIÓN (Cambia el puerto aquí si es necesario)
        // ============================================================
        private const string API_BASE = "http://localhost:5000";

        // --- Estado ---
        private string _currentIp = string.Empty;
        private bool _isEventoGuardado = false;
        private AltaEventoModel? _previousEvento; // Para detectar cambios en el objeto Evento

        private bool IsLive { get; set; } = true;
        private bool IsLoading { get; set; } = false;
        private bool ShowNoSignal { get; set; } = false;
        public string StatusMessage { get; set; } = string.Empty; // Nuevo: mensaje de estado para la UI

        // --- Imágenes y Buffer ---
        private string ImageSource { get; set; } = string.Empty;
        private List<string> FramesBuffer { get; set; } = new();

        // --- Slider ---
        private int SliderValue { get; set; } = 100;
        private int SliderMax { get; set; } = 100;
        private string LabelStart { get; set; } = "";
        private string LabelCurrent { get; set; } = "Tiempo Real";

        // --- Timers ---
        private System.Timers.Timer? _watchdogTimer;
        private System.Timers.Timer? _loopTimer;
        private const int HISTORY_BUFFER_SIZE = 600;
        private const int SIGNAL_TIMEOUT_SECONDS = 10;
        private const int EVENTO_FRAMES = 10;

        // --- Estado para guardar evento ---
        private bool _mostrandoDialogoGuardar = false;
        private string _nombreEvento = "";
        private string _descripcionEvento = "";

        // Opciones para ignorar mayúsculas/minúsculas en JSON
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        protected override void OnInitialized()
        {
            _watchdogTimer = new System.Timers.Timer(2000);
            _watchdogTimer.Elapsed += async (sender, e) => await CheckHealth();
            _watchdogTimer.AutoReset = true;
        }

        protected override async Task OnParametersSetAsync()
        {
            // Si el Evento cambia a null (deselección), reseteamos el estado
            if (Evento == null)
            {
                _previousEvento = null;
                _currentIp = string.Empty;
                _isEventoGuardado = false;
                IsLive = true;
                IsLoading = false;
                ShowNoSignal = false;
                FramesBuffer.Clear();
                _watchdogTimer?.Stop();
                _loopTimer?.Stop();
                StatusMessage = "Seleccione una cámara o evento para visualizar.";
                await InvokeAsync(StateHasChanged); // Forzar re-renderizado
                return;
            }

            // Detectar si el objeto Evento es una referencia diferente
            bool eventoObjetoCambio = Evento != _previousEvento;

            // Detectar si cambió la cámara o el tipo de evento (vivo vs guardado)
            bool cambioCamara = Evento.IpCamara != _currentIp;
            bool esGuardado = Evento.FromFrame != null && Evento.ToFrame != null;

            if (eventoObjetoCambio || cambioCamara || esGuardado != _isEventoGuardado)
            {
                _previousEvento = Evento; // Actualizar la referencia del evento anterior
                _currentIp = Evento.IpCamara;
                _isEventoGuardado = esGuardado;
                
                StatusMessage = "Cargando..."; // Mensaje de carga inicial
                // Forzar un re-renderizado rápido para mostrar el estado de carga/cambio
                await InvokeAsync(StateHasChanged);

                if (_isEventoGuardado)
                {
                    await PlayEventoGuardado(); // Ahora es async Task
                }
                else
                {
                    await GoLive(); // Ahora es async Task
                }
            }
            await base.OnParametersSetAsync(); // Llama a la implementación base para otros parámetros
        }

        // Nuevo método para forzar la carga/reproducción
        private async Task ForcePlaySelectedEvent()
        {
            if (Evento == null)
            {
                StatusMessage = "No hay ningún evento o cámara seleccionada para cargar.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            StatusMessage = "Forzando carga...";
            await InvokeAsync(StateHasChanged);

            if (_isEventoGuardado)
            {
                await PlayEventoGuardado();
            }
            else
            {
                await GoLive();
            }
        }


        private async Task GoLive()
        {
            IsLive = true;
            IsLoading = false;
            ShowNoSignal = false;
            FramesBuffer.Clear(); // Limpiar el buffer antes de ir a vivo
            _loopTimer?.Stop();

            SliderMax = 100;
            SliderValue = 100;
            LabelCurrent = "Tiempo Real";
            LabelStart = "";

            UpdateStreamUrl();
            _watchdogTimer?.Start();
            StatusMessage = "Stream en vivo iniciado.";
            await InvokeAsync(StateHasChanged); // Forzar re-renderizado
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
            StatusMessage = "Reproduciendo historial.";
            await InvokeAsync(StateHasChanged); // Forzar re-renderizado
        }

        private void TogglePlayPause()
        {
            if (_isEventoGuardado) return;
            if (IsLive) _ = GoPause(); // No await aquí para no bloquear la UI
            else _ = GoLive(); // No await aquí
        }

        private async Task LoadHistoryBuffer()
        {
            try
            {
                FramesBuffer.Clear(); // Limpiar el buffer antes de cargar
                var url = $"{API_BASE}/api/buffer/{_currentIp}?count={HISTORY_BUFFER_SIZE}";
                var snapshot = await Http.GetFromJsonAsync<HistorySnapshotDto>(url, _jsonOptions);

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
                System.Console.WriteLine($"Error buffer: {ex.Message}");
                LabelCurrent = "Error historial";
                StatusMessage = $"Error al cargar el buffer de historial: {ex.Message}";
            }
            await InvokeAsync(StateHasChanged); // Forzar re-renderizado
        }

        // ========== REPRODUCCIÓN DE EVENTO (CORREGIDO) ==========
        private async Task PlayEventoGuardado() // Cambiado a async Task
        {
            // CORRECCIÓN: Usamos FromFrame y ToFrame
            if (Evento == null || !Evento.FromFrame.HasValue || !Evento.ToFrame.HasValue)
            {
                StatusMessage = "Error: Datos de evento incompletos para la reproducción.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            IsLive = false;
            _watchdogTimer?.Stop();
            _loopTimer?.Stop();
            ShowNoSignal = false;
            IsLoading = true;
            FramesBuffer.Clear(); // Limpiar el buffer antes de cargar el evento
            StatusMessage = $"Cargando evento '{Evento.Nombre}'...";
            await InvokeAsync(StateHasChanged);

            try
            {
                // CORRECCIÓN: Usamos las propiedades correctas en la URL
                var url = $"{API_BASE}/api/range/{_currentIp}?from={Evento.FromFrame}&to={Evento.ToFrame}";
                var snapshot = await Http.GetFromJsonAsync<HistorySnapshotDto>(url, _jsonOptions);

                if (snapshot != null && snapshot.Files.Count > 0)
                {
                    FramesBuffer = snapshot.Files;
                    SliderMax = FramesBuffer.Count - 1;
                    SliderValue = 0;
                    LabelStart = "Evento Guardado";
                    LabelCurrent = $"{FramesBuffer.Count} frames";

                    StartLoopPlayback();
                    StatusMessage = $"Evento '{Evento.Nombre}' cargado correctamente ({FramesBuffer.Count} frames).";
                }
                else
                {
                    LabelCurrent = "Evento sin frames";
                    StatusMessage = $"Error: El evento '{Evento.Nombre}' no tiene frames disponibles o la API no retornó datos.";
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error evento: {ex.Message}");
                LabelCurrent = "Error evento";
                StatusMessage = $"Error al cargar el evento '{Evento.Nombre}': {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                await InvokeAsync(StateHasChanged); // Forzar re-renderizado
            }
        }

        private void StartLoopPlayback()
        {
            if (FramesBuffer.Count == 0) return;

            int currentIndex = 0;
            _loopTimer?.Stop();
            _loopTimer = new System.Timers.Timer(250); // 4 FPS para que se vea bien

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

        // ========== GUARDAR EVENTO (CORREGIDO) ==========
        public void MostrarDialogoGuardar()
        {
            // No permitimos guardar si ya es un evento guardado
            if (Evento == null || Evento.FromFrame != null) return;

            _mostrandoDialogoGuardar = true;
            _nombreEvento = $"Evento {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            _descripcionEvento = "";
            StateHasChanged();
        }

        public void CancelarGuardar()
        {
            _mostrandoDialogoGuardar = false;
            StateHasChanged();
        }

        public async Task ConfirmarGuardarEvento()
        {
            if (Evento == null || string.IsNullOrWhiteSpace(_nombreEvento)) return;

            try
            {
                var rangeInfo = await Http.GetFromJsonAsync<RangeInfoDto>(
                    $"{API_BASE}/api/range-info/{_currentIp}", _jsonOptions);

                if (rangeInfo != null && rangeInfo.MaxNumber > 0)
                {
                    // Calculamos el rango de los últimos frames
                    int frameFin = rangeInfo.MaxNumber;
                    int frameInicio = frameFin - EVENTO_FRAMES + 1;
                    if (frameInicio < rangeInfo.MinNumber) frameInicio = rangeInfo.MinNumber;

                    var nuevoEvento = new AltaEventoModel
                    {
                        Nombre = _nombreEvento,
                        IpCamara = Evento.IpCamara,
                        Puerto = Evento.Puerto,
                        FromFrame = frameInicio,
                        ToFrame = frameFin,
                        FechaEvento = DateTime.Now,
                        Descripcion = _descripcionEvento
                    };

                    var response = await Http.PostAsJsonAsync($"{API_BASE}/api/eventos/guardar", nuevoEvento);

                    if (response.IsSuccessStatusCode)
                    {
                        var eventoCreado = await response.Content.ReadFromJsonAsync<AltaEventoModel>(_jsonOptions);
                        await OnGuardarEvento.InvokeAsync(eventoCreado);
                        _mostrandoDialogoGuardar = false;
                        StatusMessage = $"Evento '{_nombreEvento}' guardado exitosamente.";
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        StatusMessage = $"Error al guardar evento: {errorContent}";
                        System.Console.WriteLine($"Error al guardar evento: {errorContent}");
                    }
                }
                else
                {
                    StatusMessage = "Error: No se pudo obtener información de rango para guardar el evento.";
                    System.Console.WriteLine("Error: No se pudo obtener información de rango para guardar el evento.");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error de conexión o inesperado al guardar evento: {ex.Message}";
                System.Console.WriteLine($"Error de conexión o inesperado al guardar evento: {ex.Message}");
            }
            finally
            {
                await InvokeAsync(StateHasChanged);
            }
        }

        private void OnSliderInput(ChangeEventArgs e)
        {
            if (IsLive || _isEventoGuardado) return;
            if (int.TryParse(e.Value?.ToString(), out int val))
            {
                SliderValue = val;
                ShowFrame(val);
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

        private async Task CheckHealth()
        {
            if (!IsLive || string.IsNullOrEmpty(_currentIp) || _isEventoGuardado) return;
            try
            {
                var url = $"{API_BASE}/api/camaras/health/{_currentIp}";
                var health = await Http.GetFromJsonAsync<HealthDto>(url, _jsonOptions);
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

        private void UpdateStreamUrl()
        {
            ImageSource = $"{API_BASE}/api/stream/{_currentIp}?t={DateTime.Now.Ticks}";
        }

        private void HandleImageError()
        {
            if (IsLive && !_isEventoGuardado)
            {
                Task.Delay(3000).ContinueWith(_ =>
                {
                    UpdateStreamUrl();
                    InvokeAsync(StateHasChanged);
                });
            }
        }

        public void Dispose()
        {
            _watchdogTimer?.Stop(); _watchdogTimer?.Dispose();
            _loopTimer?.Stop(); _loopTimer?.Dispose();
        }

        // --- DTOs ---
        private class HistorySnapshotDto
        {
            public string HistoryId { get; set; } = "";
            public List<string> Files { get; set; } = new();
        }
        private class HealthDto
        {
            public double SecondsAgo { get; set; }
        }
        private class RangeInfoDto
        {
            public int MinNumber { get; set; }
            public int MaxNumber { get; set; }
            public int TotalFrames { get; set; }
        }
    }
}
