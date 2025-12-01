using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Models;
using TelnetInterceptor.Worker.Data;

namespace TelnetInterceptor.Worker.Services
{
    public class EventStorageService : IEventStorageService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public EventStorageService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task SaveEventAsync(AltaEventoModel evento)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.EventosGuardados.Add(evento);
            await dbContext.SaveChangesAsync();
        }

        public async Task<List<AltaEventoModel>> GetEventsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await dbContext.EventosGuardados.ToListAsync();
        }

        public async Task<List<AltaEventoModel>> GetEventsByCameraIpAsync(string cameraIp)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await dbContext.EventosGuardados
                                 .Where(e => e.IpCamara == cameraIp)
                                 .ToListAsync();
        }

        public async Task ClearAllEventsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Borrar todos los eventos
            dbContext.EventosGuardados.RemoveRange(dbContext.EventosGuardados);
            await dbContext.SaveChangesAsync();
        }
    }
}
