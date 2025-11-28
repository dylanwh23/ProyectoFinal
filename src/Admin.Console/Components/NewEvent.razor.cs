using Microsoft.AspNetCore.Components;
using System.Net.Sockets; 
using System.Net.Http.Json; 
using Shared.Contracts.Models;

namespace Admin.Console.Components
{
    public partial class NewEvent : ComponentBase
    {
        [Inject]
        public ILogger<NewEvent> Logger { get; set; } = default!;

        // Eliminamos NavigationManager

        [SupplyParameterFromForm]
        public EventViewModel FormModel { get; set; } = new();

        [Inject]
        private HttpClient Http { get; set; } = default!;

        // Estados visuales
        private bool? camConnectionStatus = null;
        private bool isLoadingCam = false;

        private bool? pcConnectionStatus = null;
        private bool isLoadingPc = false;
        
        // Estado del envío
        private bool isSubmitting = false;
        private string? errorMessage = null;
        private string? successMessage = null; // Nuevo: Feedback visual en la misma página

        // --- TEST CÁMARA ---
        private async Task TestCameraConnection()
        {
            string ip = FormModel.GetCameraIpString();
            int port = FormModel.CamPort;

            isLoadingCam = true;
            camConnectionStatus = null; 
            StateHasChanged(); 

            camConnectionStatus = await TryConnectAsync(ip, port);

            isLoadingCam = false;
        }

        private async Task TestFolderAccess()
        {
            string ruta = FormModel.RutaCarpeta;
            isLoadingPc = true;
            pcConnectionStatus = null;
            StateHasChanged();

            pcConnectionStatus = await Task.Run(() => TryAccessFolder(ruta));

            isLoadingPc = false;
        }

        private bool TryAccessFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                if (!Directory.Exists(path)) return false;
                
                // Prueba rápida de lectura
                var archivos = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly).Take(1).ToList();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TryConnectAsync(string ip, int port)
        {
            if (string.IsNullOrWhiteSpace(ip) || ip.Contains(".0.0.0") || port <= 0) return false;

            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(2000);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask) return false; 

                await connectTask; 
                return true;
            }
            catch
            {
                return false;
            }
        }

        // --- SUBMIT HTTP ---
        private async Task Submit()
        {
            isSubmitting = true;
            errorMessage = null;
            successMessage = null;
            StateHasChanged();

            try
            {
                // Objeto anónimo que coincide con el DTO del Backend (CamaraRequest)
                var nuevoEvento = new
                {
                    IpCamara = FormModel.GetCameraIpString(),
                    Puerto = FormModel.CamPort,
                    RutaCarpeta = FormModel.RutaCarpeta,
                    Nombre = FormModel.Nombre // Asegúrate que tu backend reciba este campo ahora
                };

                Logger.LogInformation("Enviando POST...");

                var response = await Http.PostAsJsonAsync("api/camaras", nuevoEvento);

                if (response.IsSuccessStatusCode)
                {
                    Logger.LogInformation("Evento registrado.");
                    successMessage = "✅ Evento registrado correctamente.";                   
                    FormModel = new EventViewModel();
                    // camConnectionStatus = null;
                    // pcConnectionStatus = null;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    errorMessage = $"Error del servidor: {errorContent}";
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Error de conexión: {ex.Message}";
                Logger.LogError(ex, "Fallo al enviar");
            }
            finally
            {
                isSubmitting = false;
                StateHasChanged();
            }
        }

        public class EventViewModel
        {
            public string Nombre { get; set; } = string.Empty;

            public int CamIp1 { get; set; } = 192;
            public int CamIp2 { get; set; } = 168;
            public int CamIp3 { get; set; } = 1;
            public int CamIp4 { get; set; } = 10;
            public int CamPort { get; set; } = 23;

            public string RutaCarpeta { get; set; } = string.Empty;

            public string GetCameraIpString() => $"{CamIp1}.{CamIp2}.{CamIp3}.{CamIp4}";
        }
    }
}