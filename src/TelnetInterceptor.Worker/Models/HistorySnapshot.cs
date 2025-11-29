namespace TelnetInterceptor.Worker.Models;

public class HistorySnapshot
{
    public string HistoryId { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new List<string>();

    // Nuevos campos para el rango
    public int? FromNumber { get; set; }
    public int? ToNumber { get; set; }
}