using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EventProcessor.Worker.Services;

public class WebhookOptions
{
    public bool Enabled { get; set; } = false;
    public List<string> Urls { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 10;
    public int Retries { get; set; } = 3;
}

public class WebhookService
{
    private readonly HttpClient _httpClient;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public WebhookService(HttpClient httpClient, IOptions<WebhookOptions> options, ILogger<WebhookService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendWebhookAsync(Shared.Contracts.Models.EnrichedEvent enrichedEvent)
    {
        if (!_options.Enabled || _options.Urls.Count == 0)
        {
            _logger.LogDebug("Webhooks deshabilitados o sin URLs configuradas.");
            return;
        }

        var jsonPayload = JsonSerializer.Serialize(enrichedEvent, _jsonOptions);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        foreach (var url in _options.Urls)
        {
            await SendToUrlAsync(url, content, enrichedEvent.EventId);
        }
    }

    private async Task SendToUrlAsync(string url, StringContent content, string eventId)
    {
        for (int attempt = 1; attempt <= _options.Retries; attempt++)
        {
            try
            {
                _logger.LogInformation("Enviando webhook para evento {EventId} a {Url} (intento {Attempt})", eventId, url, attempt);
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation("Webhook enviado exitosamente a {Url} para evento {EventId}", url, eventId);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error enviando webhook a {Url} para evento {EventId} (intento {Attempt})", url, eventId, attempt);
                if (attempt == _options.Retries)
                {
                    _logger.LogError("Fallaron todos los intentos para webhook a {Url} evento {EventId}", url, eventId);
                }
            }
        }
    }
}