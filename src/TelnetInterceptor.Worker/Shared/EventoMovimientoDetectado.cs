namespace Shared.Contracts;

// Define el formato del mensaje que se enviará a RabbitMQ
public class EventoMovimientoDetectado
{
    // Creado al interceptar el evento (TimeStamp)
    public required DateTime Momento { get; set; }

    // Categorización: IP de la cámara de origen
    public required string IpCamara { get; set; }
    
    // Contenido completo recibido de la cámara
    public required string MensajeCrudoEvento { get; set; }
}