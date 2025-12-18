using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Models;
using System.Collections.Concurrent;
using System.Threading;
using TelnetInterceptor.Worker.Data;

namespace TelnetInterceptor.Worker.Services
{
    public class EventStorageService : IEventStorageService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        private const int MaxTransientEvents = 500;
        private static readonly ConcurrentQueue<PalletEventModel> _palletEvents = new();
        private static readonly ConcurrentQueue<CamionEventModel> _camionEvents = new();
        private static int _palletPk;
        private static int _camionPk;

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

        public Task SavePalletEventAsync(PalletEventModel evento)
        {
            if (evento == null) return Task.CompletedTask;
            if (evento.Id == 0) evento.Id = Interlocked.Increment(ref _palletPk);
            _palletEvents.Enqueue(evento);
            TrimQueue(_palletEvents);
            return Task.CompletedTask;
        }

        public Task SaveCamionEventAsync(CamionEventModel evento)
        {
            if (evento == null) return Task.CompletedTask;
            if (evento.Id == 0) evento.Id = Interlocked.Increment(ref _camionPk);
            _camionEvents.Enqueue(evento);
            TrimQueue(_camionEvents);
            return Task.CompletedTask;
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

        public Task<List<PalletEventModel>> GetPalletEventsAsync()
        {
            var list = _palletEvents.ToArray()
                .OrderByDescending(e => e.FechaEvento)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<List<PalletEventModel>> GetPalletEventsByCameraIpAsync(string cameraIp)
        {
            var list = _palletEvents.ToArray()
                .Where(e => string.Equals(e.IpCamara, cameraIp, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.FechaEvento)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<List<CamionEventModel>> GetCamionEventsAsync()
        {
            var list = _camionEvents.ToArray()
                .OrderByDescending(e => e.FechaEvento)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<List<CamionEventModel>> GetCamionEventsByCameraIpAsync(string cameraIp)
        {
            var list = _camionEvents.ToArray()
                .Where(e => string.Equals(e.IpCamara, cameraIp, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.FechaEvento)
                .ToList();
            return Task.FromResult(list);
        }

        public async Task ClearAllEventsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Borrar todos los eventos
            dbContext.EventosGuardados.RemoveRange(dbContext.EventosGuardados);
            await dbContext.SaveChangesAsync();

            while (_palletEvents.TryDequeue(out _)) { }
            while (_camionEvents.TryDequeue(out _)) { }
        }

        private static void TrimQueue<T>(ConcurrentQueue<T> queue)
        {
            while (queue.Count > MaxTransientEvents && queue.TryDequeue(out _)) { }
        }
    }
}
