using Shared.Contracts.Models;

namespace WebhookConsumer.Web.Services;

public interface IWebhookEventService
{
    List<EnrichedEvent> GetAllEvents();
    void AddEvent(EnrichedEvent enrichedEvent);
    event Action<EnrichedEvent>? OnEventAdded;
}
