using System.Net.Http.Json;
using System.Text.Json;
using Shared.Contracts.Models;

namespace WMSClient.Services;

public class WmsCameraService : IWmsCameraService
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;

    public WmsCameraService(HttpClient http)
    {
        _http = http;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public string BuildFrameUrl(string cameraIp, string filePath)
    {
        var framePath = $"api/frame/{cameraIp}?file={Uri.EscapeDataString(filePath)}";
        return new Uri(_http.BaseAddress!, framePath).ToString();
    }

    public string BuildStreamUrl(string cameraIp)
    {
        var path = $"api/stream/{cameraIp}?t={DateTime.Now.Ticks}";
        return new Uri(_http.BaseAddress!, path).ToString();
    }

    public string BuildThumbnailUrl(string cameraIp, int width = 320, int height = 180, int quality = 60)
    {
        var path = $"api/thumbnail/{cameraIp}?width={width}&height={height}&quality={quality}&t={DateTime.Now.Ticks}";
        return new Uri(_http.BaseAddress!, path).ToString();
    }

    public string GetApiBase()
    {
        return _http.BaseAddress?.ToString() ?? string.Empty;
    }

    // Limit concurrent requests for heavy endpoints like buffer/frame to avoid blocking browser loading
    private static readonly System.Threading.SemaphoreSlim _thumbnailSemaphore = new(6);

    public async Task<List<AltaEventoModel>> GetCamerasAsync()
    {
        try
        {
            var camaras = await _http.GetFromJsonAsync<List<AltaEventoModel>>("api/camaras/estado", _jsonOptions);
            return camaras ?? new List<AltaEventoModel>();
        }
        catch
        {
            return new List<AltaEventoModel>();
        }
    }

    public async Task<List<AltaEventoModel>> GetEventsForCameraAsync(string cameraIp)
    {
        try
        {
            var eventos = await _http.GetFromJsonAsync<List<AltaEventoModel>>($"api/eventos/buscar/{cameraIp}", _jsonOptions);
            return eventos ?? new List<AltaEventoModel>();
        }
        catch
        {
            return new List<AltaEventoModel>();
        }
    }

        public async Task<List<AltaEventoModel>> GetGridEventsAsync()
        {
            try
            {
                var eventos = await _http.GetFromJsonAsync<List<AltaEventoModel>>("api/eventos/grid", _jsonOptions);
                return eventos ?? new List<AltaEventoModel>();
            }
            catch
            {
                return new List<AltaEventoModel>();
            }
        }

        public async Task<List<AltaEventoModel>> GetGridEventsByCameraAsync(string cameraIp, int puerto)
        {
            try
            {
                var eventos = await _http.GetFromJsonAsync<List<AltaEventoModel>>($"api/eventos/grid/{cameraIp}/{puerto}", _jsonOptions);
                return eventos ?? new List<AltaEventoModel>();
            }
            catch
            {
                return new List<AltaEventoModel>();
            }
        }

        public async Task<List<PalletEventModel>> GetPalletEventsAsync()
        {
            try
            {
                var eventos = await _http.GetFromJsonAsync<List<PalletEventModel>>("api/eventos/pallet", _jsonOptions);
                return eventos ?? new List<PalletEventModel>();
            }
            catch
            {
                return new List<PalletEventModel>();
            }
        }

        public async Task<List<PalletEventModel>> GetPalletEventsByCameraAsync(string cameraIp, int puerto)
        {
            try
            {
                var eventos = await _http.GetFromJsonAsync<List<PalletEventModel>>($"api/eventos/pallet/{cameraIp}/{puerto}", _jsonOptions);
                return eventos ?? new List<PalletEventModel>();
            }
            catch
            {
                return new List<PalletEventModel>();
            }
        }

        public async Task<List<CamionEventModel>> GetCamionEventsAsync()
        {
            try
            {
                var eventos = await _http.GetFromJsonAsync<List<CamionEventModel>>("api/eventos/camion", _jsonOptions);
                return eventos ?? new List<CamionEventModel>();
            }
            catch
            {
                return new List<CamionEventModel>();
            }
        }

        public async Task<List<CamionEventModel>> GetCamionEventsByCameraAsync(string cameraIp, int puerto)
        {
            try
            {
                var eventos = await _http.GetFromJsonAsync<List<CamionEventModel>>($"api/eventos/camion/{cameraIp}/{puerto}", _jsonOptions);
                return eventos ?? new List<CamionEventModel>();
            }
            catch
            {
                return new List<CamionEventModel>();
            }
        }

        public async Task<List<CamionSeccionEstadoDto>> GetCamionEstadoAsync(string? cameraIp = null, int? puerto = null)
        {
            try
            {
                var endpoint = "api/eventos/camion/estado";
                if (!string.IsNullOrWhiteSpace(cameraIp) && puerto.HasValue)
                {
                    endpoint += $"/{cameraIp}/{puerto.Value}";
                }
                var estados = await _http.GetFromJsonAsync<List<CamionSeccionEstadoDto>>(endpoint, _jsonOptions);
                return estados ?? new List<CamionSeccionEstadoDto>();
            }
            catch
            {
                return new List<CamionSeccionEstadoDto>();
            }
        }

    public async Task<List<string>> GetHistoryBufferAsync(string cameraIp, int count = 300)
    {
        await _thumbnailSemaphore.WaitAsync();
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(4));
            var response = await _http.GetAsync($"api/buffer/{cameraIp}?count={count}", cts.Token);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var snapshot = await response.Content.ReadFromJsonAsync<HistorySnapshotDto>(cancellationToken: cts.Token);
            return snapshot?.Files ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
        finally
        {
            _thumbnailSemaphore.Release();
        }
    }

    public async Task<List<string>> GetRangeFramesAsync(string cameraIp, int from, int to)
    {
        // Range frames can be bigger; allow a slightly longer timeout but avoid concurrency overload
        await _thumbnailSemaphore.WaitAsync();
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _http.GetAsync($"api/range/{cameraIp}?from={from}&to={to}", cts.Token);
            if (!response.IsSuccessStatusCode) return new List<string>();
            var snapshot = await response.Content.ReadFromJsonAsync<HistorySnapshotDto>(cancellationToken: cts.Token);
            return snapshot?.Files ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
        finally
        {
            _thumbnailSemaphore.Release();
        }
    }

    private class HistorySnapshotDto
    {
        public string HistoryId { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new List<string>();
    }

    public class CamionSeccionEstadoDto
    {
        public string IpCamara { get; set; } = string.Empty;
        public int Puerto { get; set; }
        public string Seccion { get; set; } = string.Empty;
        public string CamionId { get; set; } = string.Empty;
        public bool Ocupado { get; set; }
        public DateTime FechaEvento { get; set; }
        public string TipoEvento { get; set; } = string.Empty;
    }
}
