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
        public string fileType { get; set; }
        // MC/NC両対応: 未指定時はMC側の既定URL(/api/mc/files/upload-by-ticket)にフォールバックする。
        // NC側からの呼び出し時のみ "/api/nc/files/upload-by-ticket" 等を指定する。
        public string uploadPath { get; set; }
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
        public string debugSourcePath { get; set; }
        public string debugFolderNameSent { get; set; }
        public string debugServerRawResponse { get; set; }
        public int debugServerStatusCode { get; set; }
    }

    public class PickUploadResponse
    {
        public bool cancelled { get; set; }
        public bool success { get; set; }
        public List<UploadedFileResult> files { get; set; } = new List<UploadedFileResult>();
        public string error { get; set; }
        public string debugSelectedFolderPath { get; set; }
        public string debugOriginalFolderName { get; set; }
        public List<string> debugAllFilesFound { get; set; } = new List<string>();
        public List<string> debugFilteredFiles { get; set; } = new List<string>();
        public bool debugAttemptedWholeFolderMove { get; set; }
        public string debugWholeFolderMoveDestPath { get; set; }
        public string debugWholeFolderMoveError { get; set; }
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