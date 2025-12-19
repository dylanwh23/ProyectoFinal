using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WebhookConsumer.Web.Models;
using WebhookConsumer.Web.Services;
using Shared.Contracts.Models;

namespace WebhookConsumer.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IWebhookEventService _webhookEventService;

    public HomeController(ILogger<HomeController> logger, IWebhookEventService webhookEventService)
    {
        _logger = logger;
        _webhookEventService = webhookEventService;
    }

    public IActionResult Index()
    {
        return View(_webhookEventService.GetAllEvents());
    }

    [HttpPost("webhook")]
    public IActionResult Webhook([FromBody] EnrichedEvent enrichedEvent)
    {
        if (enrichedEvent == null)
        {
            _logger.LogWarning("Webhook received null event");
            return BadRequest("Invalid event data");
        }

        _logger.LogInformation("Received webhook for event {EventId} from camera {IpCamara}", enrichedEvent.EventId, enrichedEvent.IpCamara);
        _logger.LogInformation("Raw data: {MensajeCrudoEvento}", enrichedEvent.MensajeCrudoEvento);

        _webhookEventService.AddEvent(enrichedEvent);

        return Ok(new { status = "received", eventId = enrichedEvent.EventId });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
