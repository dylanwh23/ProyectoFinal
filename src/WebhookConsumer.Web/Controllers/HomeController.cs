using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WebhookConsumer.Web.Models;
using Shared.Contracts.Models;

namespace WebhookConsumer.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private static readonly List<EnrichedEvent> _events = new();

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View(_events);
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

        // Agregar a la lista (mantener solo los últimos 100)
        _events.Insert(0, enrichedEvent);
        if (_events.Count > 100)
        {
            _events.RemoveAt(_events.Count - 1);
        }

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
