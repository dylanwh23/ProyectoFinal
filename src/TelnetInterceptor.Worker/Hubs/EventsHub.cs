using Microsoft.AspNetCore.SignalR;

namespace TelnetInterceptor.Worker.Hubs
{
    public class EventsHub : Hub
    {
        public Task SubscribeCamera(string cameraIp)
        {
            return Groups.AddToGroupAsync(Context.ConnectionId, EventsHubGroups.Camera(cameraIp));
        }

        public Task UnsubscribeCamera(string cameraIp)
        {
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, EventsHubGroups.Camera(cameraIp));
        }
    }
}
