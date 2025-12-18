using System;
using System.ComponentModel.DataAnnotations;

namespace Admin.Console.Models;

public class NewEvent
{
    [Required(ErrorMessage = "Obligatorio")]
    [Range(0, 255, ErrorMessage = "El valor debe estar entre 0 y 255")]
    public int? octeto1 { get; set; }
    [Required(ErrorMessage = "Obligatorio")]
    [Range(0, 255, ErrorMessage = "El valor debe estar entre 0 y 255")]
    public int? octeto2 { get; set; }
    [Required(ErrorMessage = "Obligatorio")]
    [Range(0, 255, ErrorMessage = "El valor debe estar entre 0 y 255")]
    public int? octeto3 { get; set; }
    [Required(ErrorMessage = "Obligatorio")]
    [Range(0, 255, ErrorMessage = "El valor debe estar entre 0 y 255")]
    public int? octeto4 { get; set; }

    [Required(ErrorMessage = "Obligatorio")]
    [Range(0, 65535, ErrorMessage = "El valor debe estar entre 0 y 65535")]
    public int? puerto { get; set; }
}
