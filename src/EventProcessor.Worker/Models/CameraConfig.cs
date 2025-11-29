namespace EventProcessor.Worker.Models
{
    public class CameraConfig
    {
        public string IpCamara { get; set; } = string.Empty;
        public int Puerto { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string RutaCarpeta { get; set; } = string.Empty;
        public bool EstaConectada { get; set; }

        // Propiedad calculada para el nombre de la cola
        public string QueueName => IpCamara.Replace(".", "_");
    }
}
