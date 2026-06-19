using System.Collections.Generic;
namespace UploadAgent.Models
{
    public class DriveInfo2
    {
        public string letter   { get; set; }
        public string label    { get; set; }
        public string type     { get; set; }
        public double free_gb  { get; set; }
        public double total_gb { get; set; }
    }
    public class DrivesResponse { public List<DriveInfo2> drives { get; set; } = new List<DriveInfo2>(); }
}
