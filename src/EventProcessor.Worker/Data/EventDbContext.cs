using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Models;

namespace EventProcessor.Worker.Data;

public class EventDbContext : DbContext
{
    public EventDbContext(DbContextOptions<EventDbContext> options) : base(options) { }

    public DbSet<EnrichedEvent> Events { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EnrichedEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EventId).IsUnique();
            entity.HasIndex(e => e.IpCamara);
            entity.HasIndex(e => e.MomentoOriginal);
            entity.HasIndex(e => e.ProcesadoEn);

            entity.Property(e => e.MensajeCrudoEvento)
                  .HasColumnType("TEXT");

            entity.Property(e => e.VideoLink)
                  .HasMaxLength(500);
        });
    }
}
