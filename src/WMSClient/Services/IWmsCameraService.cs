using Shared.Contracts.Models;
namespace WMSClient.Services;

public interface IWmsCameraService
{
    Task<List<AltaEventoModel>> GetCamerasAsync();
    Task<List<AltaEventoModel>> GetEventsForCameraAsync(string cameraIp);
    Task<List<string>> GetHistoryBufferAsync(string cameraIp, int count = 300);
    Task<List<string>> GetRangeFramesAsync(string cameraIp, int from, int to);
    // Helper to build absolute urls to frames/stream using configured BaseAddress
    string BuildFrameUrl(string cameraIp, string filePath);
    string BuildStreamUrl(string cameraIp);
    string BuildThumbnailUrl(string cameraIp, int width = 320, int height = 180, int quality = 60);
    string GetApiBase();
}
