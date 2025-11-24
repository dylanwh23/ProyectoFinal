namespace TelnetInterceptor.Worker.Configuration;

// Clase que representa la sección "ConfiguracionInterceptor" en appsettings.json
public class ConfiguracionInterceptor
{
    
    public RabbitMQSettings RabbitMQ { get; set; } = new();

    public class RabbitMQSettings
    {
        public string Host { get; set; } = "localhost";
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public int Port { get; set; } = 5672;
    }
    // Puerto que usará el TcpListener (49211 según la cámara).
    public required int PuertoTcp { get; set; }

    // Tiempo de espera para la lógica stateful (filtrado).
    public required int SegundosCooldown { get; set; }

    // Nuevas propiedades para el control de reintentos y timeouts
    public int IntervaloReintentoSegundos { get; set; } = 5; // Valor por defecto de 5 segundos
    public int TimeoutConexionSegundos { get; set; } = 3;   // Valor por defecto de 3 segundos

    // Lista de cámaras configuradas
    public required List<ConfiguracionCamara> Camaras { get; set; }
    
}
