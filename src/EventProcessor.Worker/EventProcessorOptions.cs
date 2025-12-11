using System.ComponentModel.DataAnnotations;

namespace EventProcessor.Worker;

public class EventProcessorOptions
{
    [Required]
    public string TelnetInterceptorBaseUrl { get; set; } = "http://localhost:5000";

    [Required]
    public string RabbitMQHost { get; set; } = "localhost";

    [Required]
    public string RabbitMQUsername { get; set; } = "guest";

    [Required]
    public string RabbitMQPassword { get; set; } = "guest";

    [Required]
    public string RabbitMQExchangeName { get; set; } = "camera_events_exchange";

    [Required]
    public string ImageStreamerBaseUrl { get; set; } = "https://localhost:7000";

    [Required]
    public string JsonExportFolderPath { get; set; } = "./EventJsonExports";

    [Range(1, 65535)]
    public int JsonExportHttpPort { get; set; } = 5005;

    [Range(1, 365)]
    public int JsonExportRetentionDays { get; set; } = 7;

    [Range(1, 168)]
    public int CleanupIntervalHours { get; set; } = 24;

    public bool EnableEventSimulator { get; set; } = false;

    [Range(1, 60)]
    public int CameraDiscoveryIntervalSeconds { get; set; } = 30;

    [Range(1, 100)]
    public int MaxConcurrentHttpRequests { get; set; } = 20;
}
