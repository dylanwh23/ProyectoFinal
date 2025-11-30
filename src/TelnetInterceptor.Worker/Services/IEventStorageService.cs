using Shared.Contracts.Models;
using TelnetInterceptor.Worker.Models;

namespace TelnetInterceptor.Worker.Services
{
    public interface IEventStorageService
    {
        Task SaveEventAsync(AltaEventoModel evento);
        Task<List<AltaEventoModel>> GetEventsAsync();
        Task<List<AltaEventoModel>> GetEventsByCameraIpAsync(string cameraIp);
    }
}
