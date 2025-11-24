using System;
using Microsoft.AspNetCore.Components;
using Admin.Console.Models.Components;


namespace Admin.Console.Components
{
    public partial class Video
    {
        [Parameter]
        public EventModel? Evento { get; set; }
    }
}
