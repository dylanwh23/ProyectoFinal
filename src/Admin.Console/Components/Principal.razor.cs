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
        private int SelectedCameraPuerto { get; set; }
        private string? SelectedCameraTipo { get; set; }

        protected override void OnInitialized()
        {
        }

        private void AlSeleccionarEvento(AltaEventoModel eventoDeLaLista)
        {
            eventoSeleccionado = eventoDeLaLista;
            SelectedCameraIp = eventoDeLaLista.IpCamara; // Almacenar la IP de la cámara seleccionada
            SelectedCameraPuerto = eventoDeLaLista.Puerto;
            SelectedCameraTipo = NormalizeCameraTipo(eventoDeLaLista.TipoEvento);
            StateHasChanged();
        }

        private void VolverAVivo(AltaEventoModel camara)
        {
            eventoSeleccionado = camara;
            SelectedCameraIp = camara.IpCamara;
            SelectedCameraPuerto = camara.Puerto;
            SelectedCameraTipo = NormalizeCameraTipo(camara.TipoEvento);
            StateHasChanged();
        }

        private static string NormalizeCameraTipo(string? tipoEvento)
        {
            var t = (tipoEvento ?? string.Empty).Trim().ToLowerInvariant();
            if (t.StartsWith("grid")) return "grid";
            if (t.StartsWith("pallet")) return "pallet";
            if (t.StartsWith("camion")) return "camion";
            return "grid";
        }
    }
}
