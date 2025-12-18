using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Models;

namespace Admin.Console.Services
{
    public class RealtimeEventsService : IAsyncDisposable
    {
        private readonly ILogger<RealtimeEventsService> _logger;
        private readonly string _apiBase;
        private HubConnection? _connection;
        private bool _started;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private string? _activeCameraIp;

        public event Action<AltaEventoModel>? CameraUpserted;
        public event Action<string>? CameraDeleted;
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
                _logger.LogInformation("🔌 SignalR: Conectando a {Hub}", _apiBase);
                RegisterHandlers(_connection);
                await _connection.StartAsync();
                _started = true;
                _logger.LogInformation("✅ SignalR CONECTADO. Connection ID: {ConnectionId}", _connection.ConnectionId);

                if (!string.IsNullOrWhiteSpace(_activeCameraIp))
                {
                    await SafeInvokeAsync("SubscribeCamera", _activeCameraIp);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ SignalR falló al conectar");
                _started = false;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SetActiveCameraAsync(string? cameraIp)
        {
            await StartAsync();
            if (_connection == null) return;

            var next = string.IsNullOrWhiteSpace(cameraIp) ? null : cameraIp.Trim();
            var prev = _activeCameraIp;

            if (string.Equals(prev, next, StringComparison.OrdinalIgnoreCase)) return;

            _activeCameraIp = next;

            if (!string.IsNullOrWhiteSpace(prev))
            {
                await SafeInvokeAsync("UnsubscribeCamera", prev);
            }

            if (!string.IsNullOrWhiteSpace(next))
            {
                await SafeInvokeAsync("SubscribeCamera", next);
            }
        }

        private async Task SafeInvokeAsync(string method, string arg)
        {
            try
            {
                if (_connection == null) return;
                await _connection.InvokeAsync(method, arg);
                _logger.LogInformation("📡 [SignalR] {Method}({Arg}) OK", method, arg);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ [SignalR] {Method}({Arg}) falló", method, arg);
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
                if (!string.IsNullOrWhiteSpace(_activeCameraIp))
                {
                    await SafeInvokeAsync("SubscribeCamera", _activeCameraIp);
                }
            };

            connection.On<AltaEventoModel>("CameraUpserted", cam => 
            {
                _logger.LogInformation("📷 [SignalR] CameraUpserted: {Ip}", cam.IpCamara);
                CameraUpserted?.Invoke(cam);
            });
            connection.On<string>("CameraDeleted", ip => 
            {
                _logger.LogInformation("📷 [SignalR] CameraDeleted: {Ip}", ip);
                CameraDeleted?.Invoke(ip);
            });

            connection.On<AltaEventoModel>("InventoryEventAdded", ev => 
            {
                _logger.LogInformation("🎬 [SignalR] InventoryEventAdded: ID={Id}, Nombre={Nombre}", ev.Id, ev.Nombre);
                InventoryEventAdded?.Invoke(ev);
            });
            connection.On<AltaEventoModel>("SavedEventAdded", ev => 
            {
                _logger.LogInformation("💾 [SignalR] SavedEventAdded: ID={Id}, Nombre={Nombre}", ev.Id, ev.Nombre);
                SavedEventAdded?.Invoke(ev);
            });

            connection.On<PalletEventModel>("PalletEventAdded", ev =>
            {
                _logger.LogInformation("📦 [SignalR] PalletEventAdded: ID={Id}, Ip={Ip}", ev.Id, ev.IpCamara);
                PalletEventAdded?.Invoke(ev);
            });

            connection.On<CamionEventModel>("CamionEventAdded", ev =>
            {
                _logger.LogInformation("🚚 [SignalR] CamionEventAdded: ID={Id}, Ip={Ip}", ev.Id, ev.IpCamara);
                CamionEventAdded?.Invoke(ev);
            });

            // Compat aliases
            connection.On<AltaEventoModel>("CameraCreated", cam => 
            {
                _logger.LogInformation("📷 [SignalR compat] CameraCreated");
                CameraUpserted?.Invoke(cam);
            });
            connection.On<AltaEventoModel>("CameraUpdated", cam => 
            {
                _logger.LogInformation("📷 [SignalR compat] CameraUpdated");
                CameraUpserted?.Invoke(cam);
            });
            connection.On<string>("CameraRemoved", ip => 
            {
                _logger.LogInformation("📷 [SignalR compat] CameraRemoved");
                CameraDeleted?.Invoke(ip);
            });
            connection.On<AltaEventoModel>("EventAdded", ev => 
            {
                _logger.LogInformation("🎬 [SignalR compat] EventAdded");
                InventoryEventAdded?.Invoke(ev);
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
