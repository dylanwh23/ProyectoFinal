using WebhookConsumer.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register typed Http client for the WMS camera service using configuration
var apiBase = builder.Configuration["ApiBase"] ?? "http://localhost:5000/";
builder.Services.AddHttpClient<WebhookConsumer.Web.Services.IWmsCameraService, WebhookConsumer.Web.Services.WmsCameraService>(client =>
{
    client.BaseAddress = new Uri(apiBase);
});

// Provide a default HttpClient to Razor components
builder.Services.AddHttpClient();
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(apiBase)
});

builder.Services.AddScoped<WebhookConsumer.Web.Services.RealtimeEventsService>();
builder.Services.AddSingleton<WebhookConsumer.Web.Services.IWebhookEventService, WebhookConsumer.Web.Services.WebhookEventService>();

// Keep MVC for backward compatibility if needed
builder.Services.AddControllersWithViews();

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

app.UseRouting();
app.UseAuthorization();
app.UseAntiforgery();

// Map Blazor components
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Keep MVC routes for backward compatibility
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
