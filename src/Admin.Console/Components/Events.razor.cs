using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using Admin.Console.Models.Components;
using Shared.Contracts.Models;
using System.Text.Json;

namespace Admin.Console.Components
{
    public partial class Events : ComponentBase
    {
        [Inject] public HttpClient Http { get; set; } = default!;
        [Inject] public ILogger<Events> Logger { get; set; } = default!;
        [Parameter] public EventCallback<AltaEventoModel> OnEventoSeleccionado { get; set; }

        public List<AltaEventoModel> Camaras { get; set; } = new();
        public bool IsLoading { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            await CargarCamaras();
        }

        public async Task CargarCamaras()
        {
            IsLoading = true;
            StateHasChanged();
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                // Solo cargamos cámaras
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

        private Task SeleccionarItem(AltaEventoModel item)
        {
            return OnEventoSeleccionado.InvokeAsync(item);
        }

        public async Task EliminarCamara(string ip)
        {
            try
            {
                var response = await Http.DeleteAsync($"api/camaras/{ip}");

                if (response.IsSuccessStatusCode)
                {
                    // Quitar de la lista sin recargar todo
                    Camaras.RemoveAll(c => c.IpCamara == ip);
                }
                else
                {
                    Logger.LogWarning("No se pudo eliminar la cámara con IP {Ip}", ip);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error al eliminar cámara con IP {Ip}", ip);
            }

            StateHasChanged();
        }


    }
}