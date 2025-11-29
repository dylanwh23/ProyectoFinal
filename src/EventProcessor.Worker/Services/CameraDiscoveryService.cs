using EventProcessor.Worker.Models;
using System.Net.Http.Json;

namespace EventProcessor.Worker.Services;

public class CameraDiscoveryService(
    HttpClient httpClient,
    ILogger<CameraDiscoveryService> logger,
    IConfiguration configuration)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<CameraDiscoveryService> _logger = logger;
    private readonly string _telnetInterceptorBaseUrl = configuration["TelnetInterceptor:BaseUrl"] ?? "http://localhost:5000";

    public async Task<List<CameraConfig>> GetActiveCamerasAsync()
    {
        try
        {
            _logger.LogInformation("🔍 Descubriendo cámaras desde TelnetInterceptor...");

            var response = await _httpClient.GetAsync($"{_telnetInterceptorBaseUrl}/api/camaras");

            if (response.IsSuccessStatusCode)
            {
                var cameras = await response.Content.ReadFromJsonAsync<List<CameraConfig>>();
                _logger.LogInformation("✅ Se encontraron {Count} cámaras", cameras?.Count ?? 0);
                return cameras ?? [];
            }
            else
            {
                _logger.LogWarning("⚠️ Falló al obtener cámaras desde TelnetInterceptor: {StatusCode}", response.StatusCode);
                return [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error descubriendo cámaras");
            return [];
        }
    }

    public async Task<List<string>> GetActiveQueueNamesAsync()
    {
        var cameras = await GetActiveCamerasAsync();
        return [.. cameras
            //.Where(c => c.EstaConectada)
            .Select(c => c.QueueName)];
    }
}
