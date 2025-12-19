using Shared.Contracts.Models;

namespace WebhookConsumer.Web.Services;

public class WebhookEventService : IWebhookEventService
{
    private static readonly List<EnrichedEvent> _events = new();
    private readonly ILogger<WebhookEventService> _logger;

    public event Action<EnrichedEvent>? OnEventAdded;

    public WebhookEventService(ILogger<WebhookEventService> logger)
    {
        _logger = logger;
    }

    public List<EnrichedEvent> GetAllEvents()
    {
        lock (_events)
        {
            return new List<EnrichedEvent>(_events);
        }
    }

    public void AddEvent(EnrichedEvent enrichedEvent)
    {
        lock (_events)
        {
            _events.Insert(0, enrichedEvent);
            if (_events.Count > 100)
            {
                _events.RemoveAt(_events.Count - 1);
            }
        }

        _logger.LogInformation("✅ [WebhookEventService] Evento agregado: {EventId} de {IpCamara}", enrichedEvent.EventId, enrichedEvent.IpCamara);
        OnEventAdded?.Invoke(enrichedEvent);
    }
}
