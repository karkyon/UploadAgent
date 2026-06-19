namespace UploadAgent.Models
{
    public class HealthResponse
    {
        public string status  { get; set; } = "ok";
        public string version { get; set; } = "1.1.0";
        public string token   { get; set; }
    }
}
