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
        private List<AltaEventoModel> eventos = new();
        protected override void OnInitialized()
        {
        }
        private void AlSeleccionarEvento(AltaEventoModel eventoDeLaLista)
        {
            eventoSeleccionado = eventoDeLaLista;
        }
    }
}