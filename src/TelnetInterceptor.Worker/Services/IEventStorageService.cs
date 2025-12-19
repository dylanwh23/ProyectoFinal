using Shared.Contracts.Models;
using TelnetInterceptor.Worker.Models;

namespace TelnetInterceptor.Worker.Services
{
    public interface IEventStorageService
    {
        Task SaveEventAsync(AltaEventoModel evento);
        Task SavePalletEventAsync(PalletEventModel evento);
        Task SaveCamionEventAsync(CamionEventModel evento);
        Task<List<AltaEventoModel>> GetEventsAsync();
        Task<List<AltaEventoModel>> GetEventsByCameraIpAsync(string cameraIp);
        Task<List<PalletEventModel>> GetPalletEventsAsync();
        Task<List<PalletEventModel>> GetPalletEventsByCameraIpAsync(string cameraIp);
        Task<List<CamionEventModel>> GetCamionEventsAsync();
        Task<List<CamionEventModel>> GetCamionEventsByCameraIpAsync(string cameraIp);
        Task ClearAllEventsAsync();
        Task DeleteEventsByCameraAsync(string cameraIp);
    }
}
