namespace TelnetInterceptor.Worker.Models;

/// <summary>
/// Estado de una conexión Telnet
/// </summary>
public class TelnetConnectionStatus
{
    /// <summary>
    /// IP del dispositivo conectado
    /// </summary>
    public required string IpAddress { get; set; }

    /// <summary>
    /// Puerto de conexión
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Indica si la conexión está activa
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// Último mensaje recibido
    /// </summary>
    public string? LastMessage { get; set; }

    /// <summary>
    /// Timestamp del último mensaje
    /// </summary>
    public DateTime? LastMessageTime { get; set; }

    /// <summary>
    /// Número total de mensajes recibidos
    /// </summary>
    public int MessagesReceived { get; set; }
}