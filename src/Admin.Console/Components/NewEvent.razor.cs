using Microsoft.AspNetCore.Components;
using System.Net.Sockets; // Necesario para TcpClient
using System.IO;

namespace Admin.Console.Components
{
    public partial class NewEvent : ComponentBase
    {
        [Inject]
        public ILogger<NewEvent> Logger { get; set; } = default!;

        [SupplyParameterFromForm]
        public EventViewModel FormModel { get; set; } = new();

        // Estados visuales
        private bool? camConnectionStatus = null;
        private bool isLoadingCam = false;

        private bool? pcConnectionStatus = null;
        private bool isLoadingPc = false;

        // --- TEST CÁMARA ---
        private async Task TestCameraConnection()
        {
            string ip = FormModel.GetCameraIpString();
            int port = FormModel.CamPort;

            isLoadingCam = true;
            camConnectionStatus = null; // Reset estado
            StateHasChanged(); // Forzar render para mostrar spinner

            camConnectionStatus = await TryConnectAsync(ip, port);

            isLoadingCam = false;
        }

        private async Task TestFolderAccess()
        {
            string ruta = FormModel.RutaCarpeta;

            // Si el usuario llenó la IP de la PC pero puso una ruta local (C:\...), 
            // podrías intentar construir la ruta de red automáticamente o advertirle.
            // Por ahora, testeamos lo que escribió.

            isLoadingPc = true;
            pcConnectionStatus = null;
            StateHasChanged();

            // Ejecutamos en un hilo aparte para no bloquear la UI si la red es lenta
            pcConnectionStatus = await Task.Run(() => TryAccessFolder(ruta));

            isLoadingPc = false;
        }

        private bool TryAccessFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Logger.LogWarning("Ruta vacía.");
                return false;
            }

            try
            {
                Logger.LogInformation("Verificando acceso a carpeta: {Path}...", path);

                // 1. Verificar si existe el directorio
                if (!Directory.Exists(path))
                {
                    Logger.LogError("❌ El directorio no existe o no es accesible: {Path}", path);
                    // Tip: Si es una ruta de red, asegúrate que el usuario que corre la app .NET tenga permisos.
                    return false;
                }

                // 2. (Opcional pero recomendado) Verificar permisos de escritura/lectura reales
                // Intentamos listar archivos (prueba de lectura)
                var archivos = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly).Take(1).ToList();

                Logger.LogInformation("✅ Acceso a carpeta confirmado. Archivos detectados: {Count}", archivos.Count);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Logger.LogError("❌ Acceso denegado (Permisos). Verifica las credenciales.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError("❌ Error accediendo a la carpeta: {Message}", ex.Message);
                return false;
            }
        }

        // --- LÓGICA REUTILIZABLE (TCP CONNECT) ---
        private async Task<bool> TryConnectAsync(string ip, int port)
        {
            // Validaciones básicas antes de intentar conectar
            if (string.IsNullOrWhiteSpace(ip) || ip.Contains(".0.0.0") || port <= 0)
            {
                Logger.LogWarning("Intento de conexión con datos inválidos: {Ip}:{Port}", ip, port);
                return false;
            }

            try
            {
                Logger.LogInformation("Testeando conexión a {Ip}:{Port}...", ip, port);

                using var client = new TcpClient();
                // Timeout de 2 segundos para no congelar la UI mucho tiempo
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(2000);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Logger.LogWarning("Timeout al conectar a {Ip}:{Port}", ip, port);
                    return false; // Tiempo agotado
                }

                // Si llegamos aquí, connectTask terminó. Verificamos si fue exitoso (no lanzó excepción)
                await connectTask;

                Logger.LogInformation("✅ Conexión exitosa a {Ip}:{Port}", ip, port);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("❌ Error conectando a {Ip}:{Port}: {Message}", ip, port, ex.Message);
                return false;
            }
        }

        private void Submit()
        {
            string ipCamaraFinal = FormModel.GetCameraIpString();
            string ipPcFinal = FormModel.GetPcIpString();

            var eventoReal = new AltaEventoModel(
                ipCamaraFinal,
                ipPcFinal,
                FormModel.CamPort.ToString(),
                FormModel.RutaCarpeta,
                FormModel.Usuario,
                FormModel.Password
            );

            Logger.LogInformation(">>> SUBMIT: Cámara {Ip}:{Port} | PC {IpPc}:{PortPc}",
                eventoReal.ipCamara, eventoReal.puertoCamara, eventoReal.ipPC, FormModel.PcPort);
        }

        public class EventViewModel
        {
            // Valores por defecto para evitar 0.0.0.0 visualmente vacíos
            public int CamIp1 { get; set; } = 192;
            public int CamIp2 { get; set; } = 168;
            public int CamIp3 { get; set; } = 1;
            public int CamIp4 { get; set; } = 10;
            public int CamPort { get; set; } = 80;

            public int PcIp1 { get; set; } = 192;
            public int PcIp2 { get; set; } = 168;
            public int PcIp3 { get; set; } = 1;
            public int PcIp4 { get; set; } = 20;
            public int PcPort { get; set; } = 8080;

            public string RutaCarpeta { get; set; } = string.Empty;
            public string Usuario { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;

            public string GetCameraIpString() => $"{CamIp1}.{CamIp2}.{CamIp3}.{CamIp4}";
            public string GetPcIpString() => $"{PcIp1}.{PcIp2}.{PcIp3}.{PcIp4}";
        }
    }

    public record AltaEventoModel(string ipCamara, string ipPC, string puertoCamara, string rutaCarpetaImagenes, string usuario, string contraseña);
}