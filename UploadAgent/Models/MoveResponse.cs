using System.Collections.Generic;
namespace UploadAgent.Models
{
    public class MoveResult  { public string original { get; set; } public string destination { get; set; } public string status { get; set; } = "moved"; }
    public class MoveFailure { public string path     { get; set; } public string error       { get; set; } }
    public class MoveResponse
    {
        public bool              success { get; set; }
        public List<MoveResult>  moved   { get; set; } = new List<MoveResult>();
        public List<MoveFailure> failed  { get; set; } = new List<MoveFailure>();
    }
}
