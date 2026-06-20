using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using UploadAgent.Models;

namespace UploadAgent
{
    /// <summary>
    /// Web からの依頼を受け、Agent内で
    /// ダイアログ表示 → MachCore APIへ直接アップロード(チケット認証) → ローカル元ファイル削除
    /// までを一括して行う。
    /// </summary>
    public class UploadCoordinator
    {
        private readonly AppSettings _settings;
        private readonly AuditLogger _logger;
        private readonly StatsCounter _stats;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly System.Windows.Forms.Control _uiThreadMarshal;

        public UploadCoordinator(AppSettings settings, AuditLogger logger, StatsCounter stats, System.Windows.Forms.Control uiThreadMarshal)
        {
            _settings = settings;
            _logger = logger;
            _stats = stats;
            _uiThreadMarshal = uiThreadMarshal;
        }

        // ── 単体ファイル選択→アップロード ──────────────────────────
        public PickUploadResponse PickFileAndUpload(string ticket)
        {
            string[] paths = null;
            // OpenFileDialog はUIスレッドで実行する必要がある
            _uiThreadMarshal.Invoke(new Action(() =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "MachCore - アップロードするファイルを選択";
                    dlg.Multiselect = false;
                    dlg.Filter = "すべてのファイル (*.*)|*.*";
                    var result = dlg.ShowDialog();
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

        // ── フォルダ選択→フォルダ内全ファイルアップロード ────────────
        public PickUploadResponse PickFolderAndUpload(string ticket)
        {
            string folderPath = null;
            _uiThreadMarshal.Invoke(new Action(() =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "MachCore - アップロードするフォルダを選択";
                    var result = dlg.ShowDialog();
                    if (result == DialogResult.OK) folderPath = dlg.SelectedPath;
                }
            }));

            if (string.IsNullOrEmpty(folderPath))
            {
                _logger.Info("PICK_UPLOAD_CANCELLED (folder)");
                return new PickUploadResponse { cancelled = true, success = false };
            }

            string[] paths;
            try
            {
                paths = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                _logger.Error($"FOLDER_READ_ERROR path=\"{folderPath}\" err=\"{ex.Message}\"");
                return new PickUploadResponse { cancelled = false, success = false, error = $"フォルダ読み取り失敗: {ex.Message}" };
            }

            if (paths.Length == 0)
            {
                return new PickUploadResponse { cancelled = false, success = false, error = "フォルダ内にファイルがありません" };
            }

            return UploadFiles(ticket, paths);
        }

        // ── 共通アップロード処理（チケットはAPI側で1回限り検証されるため、複数ファイルでも同一チケットを使い回さない） ──
        // 注意: API側のワンタイムチケットは「1回消費」のため、複数ファイル送信時は1ファイル目だけが
        // チケットで認証され、2ファイル目以降は専用の追加チケットが必要になる。
        // ここでは「フォルダアップロードは複数ファイルだが、操作としては1回の依頼」という設計のため、
        // 1ファイル目はチケットを消費し、以降は同一チケットの再発行をAgent側からは行わず、
        // API側でフォルダアップロード時は単一チケットで複数ファイルを許可する設計にしている。
        private PickUploadResponse UploadFiles(string ticket, string[] paths)
        {
            var response = new PickUploadResponse { cancelled = false };
            bool firstFile = true;

            foreach (var path in paths)
            {
                try
                {
                    var fileResult = UploadSingleFile(ticket, path, firstFile);
                    firstFile = false;
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

        private UploadedFileResult UploadSingleFile(string ticket, string filePath, bool useTicket)
        {
            var fileName = Path.GetFileName(filePath);
            _logger.Info($"UPLOAD_START path=\"{filePath}\"");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                var url = _settings.MachCoreServerUrl.TrimEnd('/') + "/api/mc/files/upload-by-ticket";

                using (var content = new MultipartFormDataContent())
                using (var fileStream = File.OpenRead(filePath))
                using (var streamContent = new StreamContent(fileStream))
                {
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    content.Add(streamContent, "file", fileName);
                    content.Add(new StringContent(ticket), "ticket");

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

                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.Error($"UPLOAD_FAILED path=\"{filePath}\" status={(int)resp.StatusCode} body=\"{bodyText}\"");
                        return new UploadedFileResult { originalName = fileName, localDeleted = false, localDeleteError = $"アップロード失敗 (HTTP {(int)resp.StatusCode})" };
                    }

                    Dictionary<string, object> json;
                    try { json = _json.Deserialize<Dictionary<string, object>>(bodyText); }
                    catch { json = new Dictionary<string, object>(); }

                    int fileId = json.ContainsKey("id") ? Convert.ToInt32(json["id"]) : 0;
                    string storedName = json.ContainsKey("stored_name") ? json["stored_name"]?.ToString() : fileName;

                    _logger.Info($"UPLOAD_OK path=\"{filePath}\" fileId={fileId} storedName=\"{storedName}\"");
                    _stats.IncrementMoved(); // アップロード成功もカウント

                    // アップロード成功 → ローカル元ファイルを .machcore_trash へ移動
                    bool localDeleted = false;
                    string localDeleteError = null;
                    try
                    {
                        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(filePath));
                        var trashDir = Path.Combine(sourceDir, ".machcore_trash");
                        if (!Directory.Exists(trashDir))
                        {
                            var di = Directory.CreateDirectory(trashDir);
                            try { di.Attributes |= FileAttributes.Hidden; } catch { }
                        }
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var destName = $"{timestamp}_{fileName}";
                        var destPath = Path.Combine(trashDir, destName);
                        if (File.Exists(destPath))
                        {
                            timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssff");
                            destName = $"{timestamp}_{fileName}";
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
                        duplicateHandled = false, // API側で重複時はtrash退避されるが詳細は返さないため固定false
                        localDeleted = localDeleted,
                        localDeleteError = localDeleteError,
                    };
                }
            }
        }
    }
}
