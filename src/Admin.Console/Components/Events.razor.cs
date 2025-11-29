using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using Admin.Console.Models.Components;
using Shared.Contracts.Models;
using System.Text.Json;

namespace Admin.Console.Components
{
    public partial class Events : ComponentBase
    {
        [Inject]
        public HttpClient Http { get; set; } = default!;

        [Inject]
        public ILogger<Events> Logger { get; set; } = default!;

        // Evento para comunicar al padre (Layout)
        [Parameter]
        public EventCallback<AltaEventoModel> OnEventoSeleccionado { get; set; }

        // --- LISTAS DE DATOS ---
        // 1. Lista de cámaras en vivo
        public List<AltaEventoModel> Camaras { get; set; } = new();

        // 2. Lista de eventos históricos guardados
        public List<AltaEventoModel> EventosGuardados { get; set; } = new();

        // --- ESTADO DE LA VISTA ---
        public bool IsLoading { get; set; } = false;

        // Controla qué pestaña estamos viendo: "camaras" o "eventos"
        private string _vistaActual = "camaras";

        protected override async Task OnInitializedAsync()
        {
            await CargarTodo();
        }

        public async Task CargarTodo()
        {
            IsLoading = true;
            StateHasChanged();

            try
            {
                // 1. Configurar opciones para ignorar mayúsculas/minúsculas
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                // Cargar Cámaras (Live)
                var camaras = await Http.GetFromJsonAsync<List<AltaEventoModel>>("api/camaras/estado", options);
                if (camaras != null) Camaras = camaras;

                // 2. Cargar Eventos Guardados USANDO LAS OPCIONES
                var eventos = await Http.GetFromJsonAsync<List<AltaEventoModel>>("api/eventos/lista", options);

                if (eventos != null)
                {
                    EventosGuardados = eventos;

                    // LOG DE VERIFICACIÓN: Ahora deberían aparecer los números
                    foreach (var evt in EventosGuardados)
                    {
                        Logger.LogInformation("Evento cargado: {Nombre} Frames: {From}-{To}",
                            evt.Nombre, evt.FromFrame, evt.ToFrame);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error cargando datos");
            }
            finally
            {
                IsLoading = false;
                StateHasChanged();
            }
        }

        // Método para cambiar de pestaña desde el HTML
        public void CambiarVista(string vista)
        {
            _vistaActual = vista;
            StateHasChanged();
        }

        private Task SeleccionarItem(AltaEventoModel item)
        {
            // Log para depurar qué estamos mandando
            Logger.LogInformation("Seleccionado: {Nombre} - Frames: {From}-{To}",
                item.Nombre, item.FromFrame, item.ToFrame);

            return OnEventoSeleccionado.InvokeAsync(item);
        }




    }



}