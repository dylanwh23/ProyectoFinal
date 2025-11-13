using MassTransit;
using Microsoft.Extensions.Options;
using Shared.Contracts;
using TelnetInterceptor.Worker.Configuration;
using RabbitMQ.Client;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;
using TelnetInterceptor.Worker.Services;

namespace TelnetInterceptor.Worker.Services;

public class GestorEndpointsCamaras : IGestorEndpointsCamaras, IAsyncDisposable
{
    private readonly IBus _bus;
    private readonly ConfiguracionInterceptor _configuracion;
    private readonly ILogger<GestorEndpointsCamaras> _logger;

    private readonly ConcurrentDictionary<string, ISendEndpoint> _sendEndpoints = new();
    private readonly ConcurrentDictionary<string, ConfiguracionCamara> _camarasActivas;

    // 🔹 Mantener una única conexión y canal abiertos
    private readonly ConnectionFactory _factory;
    private IConnection? _connection;
    private IChannel? _channel;

    public GestorEndpointsCamaras(
        IBus bus,
        IOptions<ConfiguracionInterceptor> configuracion,
        ILogger<GestorEndpointsCamaras> logger) // Removed Worker from constructor
    {
        _bus = bus;
        _configuracion = configuracion.Value;
        _logger = logger;

        _camarasActivas = new ConcurrentDictionary<string, ConfiguracionCamara>(
            _configuracion.Camaras.ToDictionary(c => c.IpCamara, c => c));

        _factory = new ConnectionFactory
        {
            HostName = _configuracion.RabbitMQ.Host,
            UserName = _configuracion.RabbitMQ.Username,
            Password = _configuracion.RabbitMQ.Password,
            Port = _configuracion.RabbitMQ.Port != 0 ? _configuracion.RabbitMQ.Port : 5672
        };
    }

    // 🔹 Inicializa conexión persistente
    private async Task EnsureConnectionAsync()
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
            return;

        _connection = await _factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();

        _logger.LogInformation("🔌 Conexión persistente a RabbitMQ creada.");
    }

    public IEnumerable<string> ObtenerCamaras() => _camarasActivas.Keys.ToList();

    public ConfiguracionCamara? ObtenerCamara(string ipCamara)
    {
        _camarasActivas.TryGetValue(ipCamara, out var camara);
        return camara;
    }

    public Task<bool> AgregarCamara(string ipCamara, int puerto)
    {
        if (string.IsNullOrWhiteSpace(ipCamara))
            throw new ArgumentException("La IP de la cámara no puede estar vacía.");
        if (puerto <= 0)
            throw new ArgumentException("El puerto de la cámara debe ser un valor positivo.");

        var puertoUsar = puerto;
        var nuevaCamara = new ConfiguracionCamara { IpCamara = ipCamara, Puerto = puertoUsar };

        bool added = _camarasActivas.TryAdd(ipCamara, nuevaCamara);
        if (added)
            _logger.LogInformation("✅ Cámara agregada dinámicamente: {IpCamara}", ipCamara);

        return Task.FromResult(added);
    }

    public Task<bool> EliminarCamara(string ipCamara)
    {
        bool removed = _camarasActivas.TryRemove(ipCamara, out _);
        if (removed)
        {
            _sendEndpoints.TryRemove(ipCamara, out _);
            _logger.LogInformation("🗑️ Cámara eliminada: {IpCamara}", ipCamara);
            // Publish an event to signal the Worker to close the connection
            _ = _bus.Publish(new CameraDeletedEvent(ipCamara)); // Fire and forget
        }

        return Task.FromResult(removed);
    }

    public async Task PublicarEvento(EventoMovimientoDetectado evento, CancellationToken cancellationToken)
    {
        if (evento == null)
        {
            _logger.LogWarning("Evento nulo recibido. Se ignora.");
            return;
        }

        var ipCamara = evento.IpCamara; // No longer needs '?' as evento is checked for null
        if (string.IsNullOrWhiteSpace(ipCamara))
        {
            _logger.LogWarning("Evento sin IP de cámara. Se ignora.");
            return;
        }

        // Use MassTransit's Publish to handle routing automatically, specifying the routing key
        // If IPublishPublishContext is not available, use GetSendEndpoint and Send directly.
        var queueName = ipCamara.Replace('.', '_');
        var uri = new Uri($"queue:{queueName}");
        var endpoint = await _bus.GetSendEndpoint(uri);
        await endpoint.Send<EventoMovimientoDetectado>(evento, cancellationToken);

        _logger.LogInformation("📨 Evento enviado a la cola {QueueName} para cámara {IP}", queueName, ipCamara);
    }

    // The manual RabbitMQ client interactions for queue declaration, binding, and SendEndpoint
    // are removed as MassTransit's Publish handles this.
    // The EnsureConnectionAsync, _factory, _connection, _channel, _sendEndpoints, and DisposeAsync
    // methods might become redundant or need adjustment if they are not used by _bus.Publish.
    // For now, leaving them as they might be used by other parts of the bus or for other purposes.

    // Removed: await EnsureConnectionAsync();
    // Removed: Manual queue/exchange declaration and binding
    // Removed: _sendEndpoints logic and endpoint.Send()

    public async ValueTask DisposeAsync()
    {
        // If _connection and _channel are no longer used by _bus.Publish,
        // these can be removed. For now, leaving them in case they are used elsewhere.
        if (_channel != null)
            await _channel.CloseAsync();

        if (_connection != null)
            await _connection.CloseAsync();

        _logger.LogInformation("🔌 Conexión RabbitMQ cerrada.");
    }
}
