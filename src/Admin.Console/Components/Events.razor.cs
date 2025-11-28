using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Admin.Console.Models.Components;
using Shared.Contracts.Models;

namespace Admin.Console.Components
{
    public partial class Events : ComponentBase
    {
        [Inject]
        public HttpClient Http { get; set; } = default!;

        [Inject]
        public ILogger<Events> Logger { get; set; } = default!;

        // Evento para comunicar al padre (Layout) que se eligió una cámara
        [Parameter]
        public EventCallback<AltaEventoModel> OnEventoSeleccionado { get; set; }

        // La lista ya no es un Parámetro, es estado local
        public List<AltaEventoModel> Eventos { get; set; } = new();
        
        public bool IsLoading { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            await CargarEventos();
        }

        public async Task CargarEventos()
        {
            IsLoading = true;
            StateHasChanged();

            try
            {
                // Llamamos al backend para obtener las cámaras reales
                var resultado = await Http.GetFromJsonAsync<List<AltaEventoModel>>("api/camaras/estado");
                
                if (resultado != null)
                {
                    Eventos = resultado;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error cargando la lista de eventos");
            }
            finally
            {
                IsLoading = false;
                StateHasChanged();
            }
        }

        private Task SeleccionarEvento(AltaEventoModel evento)
        {
            Logger.LogInformation("Evento seleccionado: {Ip}", evento.IpCamara);
            return OnEventoSeleccionado.InvokeAsync(evento);
        }
    }
}