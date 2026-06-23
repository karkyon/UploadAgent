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
        public string fileType { get; set; }   // ★追加: PHOTO / DRAWING（表示用ヒント。認可はticketで行う）
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

namespace UploadAgent.Models
{
    public class PgToUsbRequest
    {
        public string ticket { get; set; }
        public string apiBaseUrl { get; set; }
    }

    public class PgToUsbResponse
    {
        public bool success { get; set; }
        public List<string> copiedFiles { get; set; } = new List<string>();
        public string destPath { get; set; }
        public string error { get; set; }
    }
}