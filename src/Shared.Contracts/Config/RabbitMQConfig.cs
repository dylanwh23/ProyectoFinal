namespace Shared.Contracts.Config;

public class RabbitMQConfig
{
    public string Host { get; set; } = "localhost";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string QueueName { get; set; } = "camera_events";
    public string ExchangeName { get; set; } = "camera_events_exchange";
}
