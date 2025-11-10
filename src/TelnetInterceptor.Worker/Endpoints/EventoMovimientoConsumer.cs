using MassTransit;
using Shared.Contracts;

namespace TelnetInterceptor.Worker.Endpoints;

public class EventoMovimientoConsumer : IConsumer<EventoMovimientoDetectado>
{
    private readonly string _ipCamara;
    private readonly ILogger<EventoMovimientoConsumer> _logger;

    public EventoMovimientoConsumer(string ipCamara, ILogger<EventoMovimientoConsumer> logger)
    {
        _ipCamara = ipCamara ?? throw new ArgumentNullException(nameof(ipCamara));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<EventoMovimientoDetectado> context)
    {
        var evento = context.Message;

        // Verificar que el evento corresponde a esta cámara
        if (evento.IpCamara != _ipCamara)
        {
            _logger.LogWarning("Se recibió un evento para la cámara {ipEvento} en el consumidor de {ipConsumidor}", 
                evento.IpCamara, _ipCamara);
            return;
        }

        _logger.LogInformation(
            "Procesando evento de movimiento para cámara {ip}: Momento={momento}, Mensaje={mensaje}",
            evento.IpCamara,
            evento.Momento,
            evento.MensajeCrudoEvento);

        // Aquí puedes agregar la lógica adicional para procesar el evento
        await Task.CompletedTask;
    }
}