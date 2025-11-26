using System;
using Microsoft.AspNetCore.Components;
using Admin.Console.Models.Components;
using Microsoft.AspNetCore.Components.Web;


namespace Admin.Console.Components
{
    public partial class Principal
    {
        private EventModel? eventoSeleccionado;
        private List<EventModel> eventos = new();
        protected override void OnInitialized()
        {
            // Datos de ejemplo para eventos
            eventos = new List<EventModel>
            {
                new EventModel { IpCamera = "192.168.1.1", IpCarpeta = "Carpeta1", RutaCarpeta = "/ruta/carpeta1", NombreEvento = "Evento1" },
                new EventModel { IpCamera = "192.168.1.2", IpCarpeta = "Carpeta2", RutaCarpeta = "/ruta/carpeta2", NombreEvento = "Evento2" }
            };
        }
        private void AlSeleccionarEvento(EventModel eventoDeLaLista)
        {
            eventoSeleccionado = eventoDeLaLista;
        }
    }
}