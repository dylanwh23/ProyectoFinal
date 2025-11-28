using Microsoft.AspNetCore.Components;
using Admin.Console.Models.Components;
using Shared.Contracts.Models;

namespace Admin.Console.Components
{
    public partial class Video : IDisposable
    {
        [Inject] 
        public HttpClient Http { get; set; } = default!;

        [Parameter]
        public AltaEventoModel? Evento { get; set; }

        // --- Estado ---
        private string _currentIp = string.Empty;
        private bool IsLive { get; set; } = true;
        private bool IsLoading { get; set; } = false;
        private bool ShowNoSignal { get; set; } = false;
        
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
        private const int HISTORY_BUFFER_SIZE = 600;
        private const int SIGNAL_TIMEOUT_SECONDS = 10;

        protected override void OnInitialized()
        {
            // Configurar timer para chequear salud cada 2 segundos
            _watchdogTimer = new System.Timers.Timer(2000);
            _watchdogTimer.Elapsed += async (sender, e) => await CheckHealth();
            _watchdogTimer.AutoReset = true;
        }

        protected override void OnParametersSet()
        {
            // Si cambia la cámara seleccionada
            if (Evento != null && Evento.IpCamara != _currentIp)
            {
                _currentIp = Evento.IpCamara;
                GoLive();
            }
        }

        private void GoLive()
        {
            IsLive = true;
            IsLoading = false;
            ShowNoSignal = false;
            FramesBuffer.Clear();

            // UI Reset
            SliderMax = 100;
            SliderValue = 100;
            LabelCurrent = "Tiempo Real";
            LabelStart = "";

            // Iniciar Stream (usamos timestamp para romper cache)
            UpdateStreamUrl();

            // Iniciar vigilancia
            _watchdogTimer?.Start();
        }

        private async Task GoPause()
        {
            IsLive = false;
            _watchdogTimer?.Stop(); // No chequear señal en pausa
            ShowNoSignal = false;
            IsLoading = true;
            
            // Cortar stream visualmente
            ImageSource = ""; 

            await LoadHistoryBuffer();

            IsLoading = false;
        }

        private void TogglePlayPause()
        {
            if (IsLive) _ = GoPause();
            else GoLive();
        }

        private async Task LoadHistoryBuffer()
        {
            try
            {
                // Llamada al endpoint backend: /api/buffer/{ip}?count=600
                var url = $"http://localhost:5000/api/buffer/{_currentIp}?count={HISTORY_BUFFER_SIZE}";
                
                // Nota: Asegúrate de que la URL base del HttpClient apunte a tu API .NET 8 (puerto 5000/5001)
                // Si HttpClient ya tiene BaseAddress, usa solo la ruta relativa.
                
                var snapshot = await Http.GetFromJsonAsync<HistorySnapshotDto>(url);

                if (snapshot != null && snapshot.Files.Count > 0)
                {
                    FramesBuffer = snapshot.Files;
                    SliderMax = FramesBuffer.Count - 1;
                    SliderValue = FramesBuffer.Count - 1; // Ir al final
                    
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
                System.Console.WriteLine($"Error cargando buffer: {ex.Message}");
                LabelCurrent = "Error historial";
            }
        }

        private void OnSliderInput(ChangeEventArgs e)
        {
            if (IsLive) return;
            
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
            // Encode para enviar la ruta como query string
            var encodedPath = Uri.EscapeDataString(filePath);
            
            ImageSource = $"http://localhost:5000/api/frame/{_currentIp}?file={encodedPath}";
            LabelCurrent = $"Frame -{FramesBuffer.Count - index}";
        }

        private async Task CheckHealth()
        {
            if (!IsLive || string.IsNullOrEmpty(_currentIp)) return;

            try
            {
                var url = $"http://localhost:5000/api/camaras/health/{_currentIp}";
                var health = await Http.GetFromJsonAsync<HealthDto>(url);

                bool shouldShowSignal = health != null && health.SecondsAgo > SIGNAL_TIMEOUT_SECONDS;

                if (ShowNoSignal != shouldShowSignal)
                {
                    ShowNoSignal = shouldShowSignal;
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch
            {
                // Si falla la petición health, asumimos error de red (no signal)
                if (!ShowNoSignal)
                {
                    ShowNoSignal = true;
                    await InvokeAsync(StateHasChanged);
                }
            }
        }

        private void UpdateStreamUrl()
        {
            // Apuntar a la API de streaming
            ImageSource = $"http://localhost:5000/api/stream/{_currentIp}?t={DateTime.Now.Ticks}";
        }

        private void HandleImageError()
        {
            // Si el stream se rompe, reintentar en 3 segundos si estamos en vivo
            if (IsLive)
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
            _watchdogTimer?.Stop();
            _watchdogTimer?.Dispose();
        }

        // --- DTOs internos para deserializar JSON ---
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
}