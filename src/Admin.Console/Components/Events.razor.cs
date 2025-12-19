using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using Admin.Console.Models.Components;
using Shared.Contracts.Models;
using System.Text.Json;
using Admin.Console.Services;
using Microsoft.JSInterop;

namespace Admin.Console.Components
{
    public partial class Events : ComponentBase, IAsyncDisposable
    {
        [Inject] public HttpClient Http { get; set; } = default!;
        [Inject] public ILogger<Events> Logger { get; set; } = default!;
        [Inject] public RealtimeEventsService Realtime { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Parameter] public EventCallback<AltaEventoModel> OnEventoSeleccionado { get; set; }

        public List<AltaEventoModel> Camaras { get; set; } = new();
        public bool IsLoading { get; set; } = false;

        private bool _subscribed;

        protected override async Task OnInitializedAsync()
        {
            await EnsureRealtimeAsync();
            await CargarCamaras();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                await JS.InvokeVoidAsync("blazorHelper.initEvents", DotNetObjectReference.Create(this), "Events");
            }
        }

        [JSInvokable]
        public async Task HandleAction(string component, string action, string cameraIp = null)
        {
            Logger.LogInformation("🎯 Acción recibida: {Action} para cámara {Ip}", action, cameraIp ?? "N/A");
            
            if (action == "Refresh")
            {
                await CargarCamaras();
            }
            else if (action == "Delete" && !string.IsNullOrEmpty(cameraIp))
            {
                await EliminarCamara(cameraIp);
            }
            else if (action == "Select" && !string.IsNullOrEmpty(cameraIp))
            {
                Logger.LogInformation("🔍 Buscando cámara con IP: {Ip}. Total cámaras: {Count}", cameraIp, Camaras.Count);
                var camara = Camaras.FirstOrDefault(c => c.IpCamara == cameraIp);
                if (camara != null)
                {
                    Logger.LogInformation("✅ Cámara encontrada: {Nombre}", camara.Nombre);
                    await SeleccionarItem(camara);
                }
                else
                {
                    Logger.LogWarning("⚠️ No se encontró la cámara con IP: {Ip}", cameraIp);
                }
            }
        }

        private async Task EnsureRealtimeAsync()
        {
            await Realtime.StartAsync();
            if (_subscribed) return;
            Realtime.CameraUpserted += HandleCameraUpserted;
            Realtime.CameraDeleted += HandleCameraDeleted;
            _subscribed = true;
        }

        public async Task CargarCamaras()
        {
            Logger.LogInformation("🔄 Refrescando lista de cámaras...");
            IsLoading = true;
            StateHasChanged();
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var camaras = await Http.GetFromJsonAsync<List<AltaEventoModel>>("api/camaras/estado", options);
                if (camaras != null) Camaras = camaras;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error cargando cámaras");
            }
            finally
            {
                IsLoading = false;
                StateHasChanged();
            }
        }

        private async Task EliminarCamara(string ip)
        {
            try
            {
                var response = await Http.DeleteAsync($"api/camaras/{ip}");

                if (response.IsSuccessStatusCode)
                {
                    Logger.LogInformation("Cámara {Ip} eliminada exitosamente", ip);
                    // Remover de la lista local
                    Camaras.RemoveAll(c => c.IpCamara == ip);
                    StateHasChanged();
                }
                else
                {
                    Logger.LogWarning("Error al eliminar cámara {Ip}: {StatusCode}", ip, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error eliminando cámara {Ip}", ip);
            }
        }

        private async Task SeleccionarItem(AltaEventoModel item)
        {
            Logger.LogInformation("📹 Seleccionando cámara: {Nombre} ({Ip})", item.Nombre, item.IpCamara);
            Logger.LogInformation("📤 Invocando OnEventoSeleccionado...");
            await OnEventoSeleccionado.InvokeAsync(item);
            Logger.LogInformation("✅ OnEventoSeleccionado invocado exitosamente");
            StateHasChanged();
        }

        private async void HandleCameraUpserted(AltaEventoModel cam)
        {
            var existing = Camaras.FirstOrDefault(c => c.IpCamara == cam.IpCamara);
            if (existing != null)
            {
                Camaras.Remove(existing);
            }
            Camaras.Add(cam);
            await InvokeAsync(StateHasChanged);
        }

        private async void HandleCameraDeleted(string ip)
        {
            Camaras.RemoveAll(c => c.IpCamara == ip);
            await InvokeAsync(StateHasChanged);
        }

        public override async Task SetParametersAsync(ParameterView parameters)
        {
            await base.SetParametersAsync(parameters);
            await EnsureRealtimeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (_subscribed)
            {
                Realtime.CameraUpserted -= HandleCameraUpserted;
                Realtime.CameraDeleted -= HandleCameraDeleted;
                _subscribed = false;
            }
            await Task.CompletedTask;
        }
    }
}