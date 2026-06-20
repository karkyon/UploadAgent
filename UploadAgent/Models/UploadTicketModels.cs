using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UploadAgent.Models
{
    public class PickUploadRequest
    {
        public string ticket { get; set; }
    }

    public class UploadedFileResult
    {
        public string originalName { get; set; }
        public string storedName { get; set; }
        public int fileId { get; set; }
        public bool duplicateHandled { get; set; }
        public string duplicateMovedTo { get; set; }
        public bool localDeleted { get; set; }
        public string localDeleteError { get; set; }
    }

    public class PickUploadResponse
    {
        public bool cancelled { get; set; }
        public bool success { get; set; }
        public List<UploadedFileResult> files { get; set; } = new List<UploadedFileResult>();
        public string error { get; set; }
    }
}
