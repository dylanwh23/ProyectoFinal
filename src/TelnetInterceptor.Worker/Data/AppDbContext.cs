using TelnetInterceptor.Worker.Models;
using Microsoft.EntityFrameworkCore;

namespace TelnetInterceptor.Worker.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Esta propiedad representa la tabla en la base de datos
    public DbSet<EstadisticasCamara> Eventos { get; set; }

    // Configuración opcional
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Opcional: Esto ayuda a evitar errores de bloqueo en SQLite con mucha concurrencia
        // optionsBuilder.EnableSensitiveDataLogging(); 
    }
}