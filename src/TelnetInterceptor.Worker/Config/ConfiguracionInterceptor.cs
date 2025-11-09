namespace TelnetInterceptor.Worker.Configuration;

// Clase que representa la sección "ConfiguracionInterceptor" en appsettings.json
public class ConfiguracionInterceptor
{
    // Usamos 'required' de C# 11 para asegurar que estos valores estén en el appsettings.json
    // El IPEscucha de 0.0.0.0 asegura la conectividad en Docker o VPN.
    public required string IpEscucha { get; set; }

    // Puerto que usará el TcpListener (49211 según la cámara).
    public required int PuertoTcp { get; set; }

    // Tiempo de espera para la lógica stateful (filtrado).
    public required int SegundosCooldown { get; set; }
}