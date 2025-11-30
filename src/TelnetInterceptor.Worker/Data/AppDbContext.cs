using TelnetInterceptor.Worker.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Models; // Añadir esto

namespace TelnetInterceptor.Worker.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Estas propiedades representan las tablas en la base de datos
    public DbSet<EstadisticasCamara> Eventos { get; set; }
    public DbSet<AltaEventoModel> EventosGuardados { get; set; } // Añadir esto

    // Configuración opcional
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Opcional: Esto ayuda a evitar errores de bloqueo en SQLite con mucha concurrencia
        // optionsBuilder.EnableSensitiveDataLogging(); 
    }
}
