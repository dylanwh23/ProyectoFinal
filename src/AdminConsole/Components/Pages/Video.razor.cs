using System;
using Microsoft.AspNetCore.Components;
using AdminConsole.Models.Components;


namespace AdminConsole.Components.Pages
{
    public partial class Video
    {
        [Parameter]
        public EventModel? Evento { get; set; }
    }
}
