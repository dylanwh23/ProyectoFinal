using Microsoft.AspNetCore.Components;
using Admin.Console.Models.Components;
namespace Admin.Console.Components
{
    public partial class Events : ComponentBase
    {
        [Parameter]
        public List<EventModel> Eventos { get; set; } = new();
        [Parameter]
        public EventCallback<EventModel> OnEventoSeleccionado { get; set; }
        [Inject]
        public ILogger<Events> Logger {get; set;}

        private Task SeleccionarEvento(EventModel evento)
        {
            System.Console.WriteLine($"Evento seleccionado: {evento.IpCamera}");
            return OnEventoSeleccionado.InvokeAsync(evento);
        }
        private void TestClick()
        {
            Logger.LogInformation("holaa");
        }
     
    }
}