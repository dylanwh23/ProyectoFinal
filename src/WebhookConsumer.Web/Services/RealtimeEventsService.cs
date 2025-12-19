using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Models;

namespace WebhookConsumer.Web.Services
{
    public class RealtimeEventsService : IAsyncDisposable
    {
        private readonly ILogger<RealtimeEventsService> _logger;
        private readonly string _apiBase;
        private HubConnection? _connection;
        private bool _started;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private readonly HashSet<string> _activeCameraIps = new(StringComparer.OrdinalIgnoreCase);

        public event Action<AltaEventoModel>? InventoryEventAdded;
        public event Action<AltaEventoModel>? SavedEventAdded;
        public event Action<PalletEventModel>? PalletEventAdded;
        public event Action<CamionEventModel>? CamionEventAdded;

        public RealtimeEventsService(ILogger<RealtimeEventsService> logger, IConfiguration config)
        {
            _logger = logger;
            _apiBase = config["ApiBase"] ?? "http://localhost:5000";
        }

        public async Task StartAsync()
        {
            if (_started) return;
            await _gate.WaitAsync();
            try
            {
                if (_started) return;
                _connection = BuildConnection();
                _logger.LogInformation("🔌 [WebhookConsumer] SignalR: Conectando a {Hub}", _apiBase);
                RegisterHandlers(_connection);
                await _connection.StartAsync();
                _started = true;
                _logger.LogInformation("✅ [WebhookConsumer] SignalR CONECTADO. Connection ID: {ConnectionId}", _connection.ConnectionId);

                foreach (var ip in _activeCameraIps)
                {
                    await SafeInvokeAsync("SubscribeCamera", ip);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [WebhookConsumer] SignalR falló al conectar");
                _started = false;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SetActiveCameraAsync(string? cameraIp)
        {
            var list = string.IsNullOrWhiteSpace(cameraIp) ? Array.Empty<string>() : new[] { cameraIp.Trim() };
            await SetActiveCamerasAsync(list);
        }

        public async Task SetActiveCamerasAsync(IEnumerable<string> cameraIps)
        {
            await StartAsync();
            if (_connection == null) return;

            var nextSet = new HashSet<string>(
                cameraIps
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var toUnsubscribe = _activeCameraIps.Except(nextSet, StringComparer.OrdinalIgnoreCase).ToList();
            var toSubscribe = nextSet.Except(_activeCameraIps, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var ip in toUnsubscribe)
            {
                await SafeInvokeAsync("UnsubscribeCamera", ip);
                _activeCameraIps.Remove(ip);
            }

            foreach (var ip in toSubscribe)
            {
                await SafeInvokeAsync("SubscribeCamera", ip);
                _activeCameraIps.Add(ip);
            }
        }

        private async Task SafeInvokeAsync(string method, string arg)
        {
            try
            {
                if (_connection == null) return;
                await _connection.InvokeAsync(method, arg);
                _logger.LogInformation("📡 [WebhookConsumer SignalR] {Method}({Arg}) OK", method, arg);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ [WebhookConsumer SignalR] {Method}({Arg}) falló", method, arg);
            }
        }

        private HubConnection BuildConnection()
        {
            var hubUrl = _apiBase.TrimEnd('/') + "/eventsHub";
            return new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();
        }

        private void RegisterHandlers(HubConnection connection)
        {
            connection.Reconnected += async _ =>
            {
                foreach (var ip in _activeCameraIps)
                {
                    await SafeInvokeAsync("SubscribeCamera", ip);
                }
            };

            connection.On<AltaEventoModel>("InventoryEventAdded", ev => 
            {
                _logger.LogInformation("🎬 [WebhookConsumer SignalR] InventoryEventAdded: ID={Id}, Nombre={Nombre}", ev.Id, ev.Nombre);
                InventoryEventAdded?.Invoke(ev);
            });
            connection.On<AltaEventoModel>("SavedEventAdded", ev => 
            {
                _logger.LogInformation("💾 [WebhookConsumer SignalR] SavedEventAdded: ID={Id}, Nombre={Nombre}", ev.Id, ev.Nombre);
                SavedEventAdded?.Invoke(ev);
            });

            connection.On<PalletEventModel>("PalletEventAdded", ev =>
            {
                _logger.LogInformation("📦 [WebhookConsumer SignalR] PalletEventAdded: ID={Id}, Ip={Ip}", ev.Id, ev.IpCamara);
                PalletEventAdded?.Invoke(ev);
            });

            connection.On<CamionEventModel>("CamionEventAdded", ev =>
            {
                _logger.LogInformation("🚚 [WebhookConsumer SignalR] CamionEventAdded: ID={Id}, Ip={Ip}", ev.Id, ev.IpCamara);
                CamionEventAdded?.Invoke(ev);
            });
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                try { await _connection.DisposeAsync(); }
                catch { /* best-effort */ }
            }
            _gate.Dispose();
        }
    }
}
