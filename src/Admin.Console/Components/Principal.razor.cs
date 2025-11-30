using System;
using Microsoft.AspNetCore.Components;
using Admin.Console.Models.Components;
using Microsoft.AspNetCore.Components.Web;
using Shared.Contracts.Models;

namespace Admin.Console.Components
{
    public partial class Principal
    {
        private AltaEventoModel? eventoSeleccionado;
        private string? SelectedCameraIp { get; set; } // Nueva propiedad para la IP de la cámara seleccionada

        protected override void OnInitialized()
        {
        }

        private void AlSeleccionarEvento(AltaEventoModel eventoDeLaLista)
        {
            eventoSeleccionado = eventoDeLaLista;
            SelectedCameraIp = eventoDeLaLista.IpCamara; // Almacenar la IP de la cámara seleccionada
        }
    }
}
