using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

var builder = WebApplication.CreateBuilder(args);

// --- Configuraci�n ---
builder.Services.Configure<ServerSettings>(builder.Configuration.GetSection("ServerSettings"));
builder.Services.AddSingleton<CameraStreamService>();
builder.Services.AddHostedService(p => p.GetRequiredService<CameraStreamService>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// --- API de STREAM (MJPG) ---
app.MapGet("/api/stream/{cameraName}", async (
    string cameraName, CameraStreamService cameraService, HttpContext http, CancellationToken ct) =>
{
    http.Response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
    while (!ct.IsCancellationRequested)
    {
        string? latestFile = cameraService.GetLatestFileForCamera(cameraName);
        if (latestFile != null && File.Exists(latestFile))
        {
            byte[]? jpegData = await ProcessImageAsync(latestFile, cameraService, null); // null historyId
            if (jpegData != null)
            {
                await http.Response.WriteAsync("--frame\r\n", ct);
                await http.Response.WriteAsync("Content-Type: image/jpeg\r\n", ct);
                await http.Response.WriteAsync($"Content-Length: {jpegData.Length}\r\n\r\n", ct);
                await http.Response.Body.WriteAsync(jpegData, ct);
                await http.Response.WriteAsync("\r\n", ct);
                await http.Response.Body.FlushAsync(ct);
            }
        }
        await Task.Delay(50, ct);
    }
});

// --- API de FRAME (�ltima foto O foto espec�fica) ---
app.MapGet("/api/frame/{cameraName}", async (
    string cameraName, CameraStreamService cameraService, HttpContext http) =>
{
    string? specificFile = http.Request.Query["file"];
    string? historyId = http.Request.Query["historyId"];
    string fileToProcess;
    if (!string.IsNullOrEmpty(specificFile) && !string.IsNullOrEmpty(historyId))
    {
        string safeHistoryId = Path.GetFileName(historyId);
        string safeFileName = Path.GetFileName(specificFile);
        fileToProcess = Path.Combine(cameraService.GetCameraPath(cameraName), safeHistoryId, safeFileName);
        if (!File.Exists(fileToProcess))
        {
            http.Response.StatusCode = 404;
            await http.Response.WriteAsync("El archivo de historial ya no existe.");
            return;
        }
    }
    else
    {
        fileToProcess = cameraService.GetLatestFileForCamera(cameraName);
        if (fileToProcess == null || !File.Exists(fileToProcess))
        {
            http.Response.StatusCode = 404;
            await http.Response.WriteAsync("No se ha detectado ninguna imagen en vivo.");
            return;
        }
    }
    byte[]? jpegData = await ProcessImageAsync(fileToProcess, cameraService, historyId);
    if (jpegData == null)
    {
        http.Response.StatusCode = 500;
        await http.Response.WriteAsync("Error al procesar la imagen.");
        return;
    }
    http.Response.ContentType = "image/jpeg";
    http.Response.ContentLength = jpegData.Length;
    await http.Response.Body.WriteAsync(jpegData);
});

// --- API para "Congelar" el historial ---
app.MapGet("/api/history/freeze/{cameraName}", (string cameraName, CameraStreamService cameraService) =>
{
    try
    {
        return Results.Ok(cameraService.FreezeHistory(cameraName));
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: $"Error al congelar el historial: {ex.Message}", statusCode: 500);
    }
});

// --- NUEVA: API para Borrar el historial ---
app.MapDelete("/api/history/cleanup/{cameraName}/{historyId}", (
    string cameraName,
    string historyId,
    CameraStreamService cameraService) =>
{
    try
    {
        cameraService.CleanupHistoryFolder(cameraName, historyId);
        return Results.Ok(new { message = "Historial borrado" });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: $"Error al borrar historial: {ex.Message}", statusCode: 500);
    }
});


// --- Funci�n de ayuda para procesar im�genes ---
async Task<byte[]?> ProcessImageAsync(string filePath, CameraStreamService cameraService, string? historyId)
{
    for (int i = 0; i < 5; i++)
    {
        try
        {
            using var image = await Image.LoadAsync(filePath);
            image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(1280, 720) }));
            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 85 });
            return ms.ToArray();
        }
        catch (IOException)
        {
            await Task.Delay(100);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error procesando {filePath}: {ex.Message}");
            return null;
        }
    }
    return null;
}

// --- NUEVA: API para "Congelar" por rango de HORA LOCAL ---
app.MapGet("/api/history/freeze-by-range-local/{cameraName}", (
    string cameraName,
    DateTime startTime, // ASP.NET parseará la hora local (sin 'Z')
    DateTime endTime,
    CameraStreamService cameraService,
    ILogger<Program> logger) =>
{
    try
    {
        // Esto es útil para depurar qué horas está recibiendo el servidor
        logger.LogInformation("Recibida solicitud de rango local: {start} a {end}", startTime, endTime);

        if (endTime <= startTime)
        {
            return Results.BadRequest("La fecha de fin debe ser posterior a la fecha de inicio.");
        }

        // Llama al nuevo método de hora local
        var snapshot = cameraService.FreezeHistoryByTimeRangeLocal(cameraName, startTime, endTime);

        if (snapshot.Files.Count == 0)
        {
            return Results.NotFound("No se encontraron imágenes para ese rango de hora local.");
        }

        return Results.Ok(snapshot);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: $"Error al congelar el historial por rango local: {ex.Message}", statusCode: 500);
    }
});

app.MapFallbackToFile("index.html");
app.Run();

// --- El Servicio de C�mara ---


// --- Clases de Configuraci�n ---
public class ServerSettings { public string WatchPath { get; set; } = "C:\\Public"; }
public class HistorySnapshot { public string HistoryId { get; set; } = string.Empty; public List<string> Files { get; set; } = new List<string>(); }