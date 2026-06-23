using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using UploadAgent.Models;

namespace UploadAgent
{
    public class UploadCoordinator
    {
        private readonly AppSettings _settings;
        private readonly AuditLogger _logger;
        private readonly StatsCounter _stats;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly System.Windows.Forms.Control _uiThreadMarshal;

        private static readonly HttpClientHandler _sslBypassHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
        };

        public UploadCoordinator(AppSettings settings, AuditLogger logger, StatsCounter stats, System.Windows.Forms.Control uiThreadMarshal)
        {
            _settings = settings;
            _logger = logger;
            _stats = stats;
            _uiThreadMarshal = uiThreadMarshal;
        }

        private IWin32Window GetOwner() => _uiThreadMarshal;

        public PickUploadResponse PickFileAndUpload(string ticket, string fileType = null)
        {
            string[] paths = null;
            _uiThreadMarshal.Invoke(new Action(() =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = fileType == "PHOTO" ? "MachCore - 📷 写真ファイルを選択"
                              : fileType == "DRAWING" ? "MachCore - 📐 図ファイルを選択"
                              : "MachCore - アップロードするファイルを選択";
                    dlg.Multiselect = false;
                    dlg.Filter = "すべてのファイル (*.*)|*.*";
                    var result = dlg.ShowDialog(GetOwner());
                    if (result == DialogResult.OK) paths = new[] { dlg.FileName };
                }
            }));

            if (paths == null || paths.Length == 0)
            {
                _logger.Info("PICK_UPLOAD_CANCELLED (file)");
                return new PickUploadResponse { cancelled = true, success = false };
            }

            return UploadFiles(ticket, paths);
        }

        public PickUploadResponse PickFolderAndUpload(string ticket, string fileType = null)
        {
            string folderPath = null;
            _uiThreadMarshal.Invoke(new Action(() =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = fileType == "PHOTO" ? "MachCore - 写真ファイルが入っているフォルダを選択"
                                     : fileType == "DRAWING" ? "MachCore - 図ファイルが入っているフォルダを選択"
                                     : "MachCore - アップロードするフォルダを選択";
                    var result = dlg.ShowDialog(GetOwner());
                    if (result == DialogResult.OK) folderPath = dlg.SelectedPath;
                }
            }));

            if (string.IsNullOrEmpty(folderPath))
            {
                _logger.Info("PICK_UPLOAD_CANCELLED (folder)");
                return new PickUploadResponse { cancelled = true, success = false };
            }

            string[] allFiles;
            try
            {
                allFiles = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                _logger.Error($"FOLDER_READ_ERROR path=\"{folderPath}\" err=\"{ex.Message}\"");
                return new PickUploadResponse { cancelled = false, success = false, error = $"フォルダ読み取り失敗: {ex.Message}" };
            }

            if (allFiles.Length == 0)
                return new PickUploadResponse { cancelled = false, success = false, error = "フォルダ内にファイルがありません" };

            string[] selectedFiles = null;
            bool gridCancelled = true;
            _uiThreadMarshal.Invoke(new Action(() =>
            {
                using (var picker = new Forms.FileGridPickerForm(folderPath, allFiles, fileType))
                {
                    picker.ShowDialog(GetOwner());
                    gridCancelled = picker.WasCancelled;
                    if (!gridCancelled) selectedFiles = picker.SelectedFiles.ToArray();
                }
            }));

            if (gridCancelled || selectedFiles == null || selectedFiles.Length == 0)
            {
                _logger.Info("PICK_UPLOAD_CANCELLED (folder grid selection)");
                return new PickUploadResponse { cancelled = true, success = false };
            }

            _logger.Info($"FOLDER_GRID_SELECTED count={selectedFiles.Length} / total={allFiles.Length}");
            return UploadFiles(ticket, selectedFiles);
        }

        private PickUploadResponse UploadFiles(string ticket, string[] paths)
        {
            var response = new PickUploadResponse { cancelled = false };

            foreach (var path in paths)
            {
                try
                {
                    var fileResult = UploadSingleFile(ticket, path);
                    response.files.Add(fileResult);
                }
                catch (Exception ex)
                {
                    _logger.Error($"UPLOAD_ERROR path=\"{path}\" err=\"{ex.Message}\"");
                    response.files.Add(new UploadedFileResult
                    {
                        originalName = Path.GetFileName(path),
                        localDeleted = false,
                        localDeleteError = ex.Message,
                    });
                }
            }

            response.success = response.files.Count > 0 && response.files.TrueForAll(f => f.fileId > 0);
            if (!response.success && string.IsNullOrEmpty(response.error))
                response.error = "一部または全部のファイルのアップロードに失敗しました";

            return response;
        }

        private UploadedFileResult UploadSingleFile(string ticket, string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            _logger.Info($"UPLOAD_START path=\"{filePath}\"");

            using (var client = new HttpClient(_sslBypassHandler, disposeHandler: false))
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                var url = _settings.MachCoreServerUrl.TrimEnd('/') + "/api/mc/files/upload-by-ticket";

                using (var content = new MultipartFormDataContent())
                using (var fileStream = File.OpenRead(filePath))
                using (var streamContent = new StreamContent(fileStream))
                {
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    content.Add(new StringContent(ticket), "ticket");
                    content.Add(streamContent, "file", fileName);

                    HttpResponseMessage resp;
                    try
                    {
                        resp = client.PostAsync(url, content).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"UPLOAD_HTTP_ERROR path=\"{filePath}\" err=\"{ex.Message}\"");
                        return new UploadedFileResult { originalName = fileName, localDeleted = false, localDeleteError = $"通信エラー: {ex.Message}" };
                    }

                    var bodyText = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    _logger.Info($"UPLOAD_RESPONSE status={(int)resp.StatusCode} body=\"{bodyText}\"");

                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.Error($"UPLOAD_FAILED path=\"{filePath}\" status={(int)resp.StatusCode} body=\"{bodyText}\"");
                        return new UploadedFileResult { originalName = fileName, localDeleted = false, localDeleteError = $"アップロード失敗 (HTTP {(int)resp.StatusCode}): {bodyText}" };
                    }

                    Dictionary<string, object> json;
                    try { json = _json.Deserialize<Dictionary<string, object>>(bodyText); }
                    catch { json = new Dictionary<string, object>(); }

                    int fileId = json.ContainsKey("id") ? Convert.ToInt32(json["id"]) : 0;
                    string storedName = json.ContainsKey("stored_name") ? json["stored_name"]?.ToString() : fileName;

                    _logger.Info($"UPLOAD_OK path=\"{filePath}\" fileId={fileId} storedName=\"{storedName}\"");
                    _stats.IncrementMoved();

                    // アップロード成功 → ローカル元ファイルを設定のTrashフォルダ（一元・設定可能）へ移動
                    bool localDeleted = false;
                    string localDeleteError = null;
                    try
                    {
                        var trashDir = _settings.GetEffectiveTrashRoot();
                        if (!Directory.Exists(trashDir)) Directory.CreateDirectory(trashDir);

                        var ext = Path.GetExtension(fileName);
                        var nameOnly = Path.GetFileNameWithoutExtension(fileName);
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        // 命名規則: 元のファイル名_yyyymmdd_hhMMss.拡張子
                        var destName = $"{nameOnly}_{timestamp}{ext}";
                        var destPath = Path.Combine(trashDir, destName);
                        if (File.Exists(destPath))
                        {
                            timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssff");
                            destName = $"{nameOnly}_{timestamp}{ext}";
                            destPath = Path.Combine(trashDir, destName);
                        }
                        File.Move(filePath, destPath);
                        localDeleted = true;
                        _logger.Info($"LOCAL_TRASH_OK src=\"{filePath}\" dst=\"{destPath}\"");
                    }
                    catch (Exception ex)
                    {
                        localDeleteError = ex.Message;
                        _logger.Warn($"LOCAL_TRASH_FAILED path=\"{filePath}\" err=\"{ex.Message}\"");
                    }

                    return new UploadedFileResult
                    {
                        originalName = fileName,
                        storedName = storedName,
                        fileId = fileId,
                        duplicateHandled = false,
                        localDeleted = localDeleted,
                        localDeleteError = localDeleteError,
                    };
                }
            }
        }
    }
}