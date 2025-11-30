using Shared.Contracts.Models;
using EventProcessor.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventProcessor.Worker.Services;

public class EventStorageService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventStorageService> _logger;

    public EventStorageService(IServiceProvider serviceProvider, ILogger<EventStorageService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<IEnumerable<EnrichedEvent>> GetRecentEventsAsync(int count = 50)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EventDbContext>();

        return await context.Events
            .OrderByDescending(e => e.MomentoOriginal)
            .Take(count)
            .ToListAsync();
    }

    public async Task<EnrichedEvent?> GetEventByIdAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EventDbContext>();

        return await context.Events
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<IEnumerable<EnrichedEvent>> GetEventsByCameraAsync(string ipCamara, DateTime from, DateTime to)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EventDbContext>();

        return await context.Events
            .Where(e => e.IpCamara == ipCamara && e.MomentoOriginal >= from && e.MomentoOriginal <= to)
            .OrderByDescending(e => e.MomentoOriginal)
            .ToListAsync();
    }
}
