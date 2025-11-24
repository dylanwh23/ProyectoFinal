using Shared.Contracts.Models;
using EventProcessor.Worker.Data;
using Microsoft.EntityFrameworkCore;

namespace EventProcessor.Worker.Services;

public class EventStorageService
{
    private readonly EventDbContext _context;
    private readonly ILogger<EventStorageService> _logger;

    public EventStorageService(EventDbContext context, ILogger<EventStorageService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IEnumerable<EnrichedEvent>> GetRecentEventsAsync(int count = 50)
    {
        return await _context.Events
            .OrderByDescending(e => e.MomentoOriginal)
            .Take(count)
            .ToListAsync();
    }

    public async Task<EnrichedEvent?> GetEventByIdAsync(int id)
    {
        return await _context.Events
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<IEnumerable<EnrichedEvent>> GetEventsByCameraAsync(string ipCamara, DateTime from, DateTime to)
    {
        return await _context.Events
            .Where(e => e.IpCamara == ipCamara && e.MomentoOriginal >= from && e.MomentoOriginal <= to)
            .OrderByDescending(e => e.MomentoOriginal)
            .ToListAsync();
    }
}
