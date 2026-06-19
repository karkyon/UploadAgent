// Models/MoveRequest.cs
using System.Collections.Generic;
namespace UploadAgent.Models
{
    public class MoveRequest
    {
        public List<string> paths       { get; set; } = new List<string>();
        public string       reason      { get; set; } = "upload_complete";
        public int?         operator_id { get; set; }
    }
}
