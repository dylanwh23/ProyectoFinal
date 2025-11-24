using System;
using Microsoft.AspNetCore.Components;
using AdminConsole.Models.Components;
using System.Diagnostics; // ⬅️ Usa Debug
namespace AdminConsole.Components.Pages
{
    public partial class Events
    {
        [Parameter]
        public List<EventModel> Eventos { get; set; } = new();
        [Parameter]
        public EventCallback<EventModel> OnEventoSeleccionado { get; set; }

        private Task SeleccionarEvento(EventModel evento)
        {
            System.Console.WriteLine($"Evento seleccionado: {evento.IpCamera}");
            return OnEventoSeleccionado.InvokeAsync(evento);
        }
     
    }
}