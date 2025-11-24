using System;
using Microsoft.AspNetCore.Components;
using Admin.Console.Models.Components;
using System.Diagnostics; // ⬅️ Usa Debug
namespace Admin.Console.Components
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