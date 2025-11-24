using Microsoft.Extensions.Options;

namespace EventProcessor.Worker.Services;

public class VideoLinkService
{
    private readonly ILogger<VideoLinkService> _logger;
    private readonly string _imageStreamerBaseUrl;

    public VideoLinkService(IConfiguration configuration, ILogger<VideoLinkService> logger)
    {
        _logger = logger;
        _imageStreamerBaseUrl = configuration["ImageStreamer:BaseUrl"] ?? "https://localhost:7000";
    }

    public string GenerateVideoLink(string ipCamara, DateTime eventTime)
    {
        try
        {
            // Convertir IP a nombre de camara (ej: "192.168.1.100" -> "cam_192_168_1_100")
            var cameraName = "cam_" + ipCamara.Replace('.', '_');

            // Formato de URL para el ImageStreamer
            var videoLink = $"{_imageStreamerBaseUrl}/api/cameras/{cameraName}/stream?timestamp={eventTime:yyyy-MM-ddTHH:mm:ss}";

            _logger.LogDebug("Generated video link for camera {Camera}: {Link}", ipCamara, videoLink);
            return videoLink;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating video link for camera {Camera}", ipCamara);
            return string.Empty;
        }
    }
}
