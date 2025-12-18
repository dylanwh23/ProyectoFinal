namespace TelnetInterceptor.Worker.Hubs;

public static class EventsHubGroups
{
    public static string Camera(string? cameraIp)
    {
        var ip = (cameraIp ?? string.Empty).Trim();
        return $"camera:{ip.ToLowerInvariant()}";
    }
}
