using WMSClient.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register typed Http client for the WMS camera service using configuration
var apiBase = builder.Configuration["ApiBase"] ?? "http://localhost:5000/";
builder.Services.AddHttpClient<WMSClient.Services.IWmsCameraService, WMSClient.Services.WmsCameraService>(client =>
{
    client.BaseAddress = new Uri(apiBase);
});

// Provide a default HttpClient to Razor components similar to Admin.Console
builder.Services.AddHttpClient();
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(apiBase)
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
