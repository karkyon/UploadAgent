using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.InteropServices;
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

        // ★追加: ダイアログをブラウザ等の他ウィンドウより確実に最前面へ出すためのWin32 API
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        private const int SW_RESTORE = 9;

        // ★追加: 拡張子からMIMEタイプを判定する。サーバ側のisImage判定等が正しく動くようにする
        private static string GetMimeType(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            switch (ext)
            {
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".tif":
                case ".tiff": return "image/tiff";
                case ".pdf": return "application/pdf";
                case ".txt": return "text/plain";
                default: return "application/octet-stream";
            }
        }

        // ★追加: ダイアログ表示前にメインウィンドウを確実にフォアグラウンド化する。
        //   SetForegroundWindowだけではOSのフォーカス窃取防止機構によって無視されることがあるため、
        //   現在フォアグラウンドにあるウィンドウ(ブラウザ等)のスレッドにAttachThreadInputで一時結合し、
        //   その上でフォアグラウンド化することで確実性を高める。
        private void ForceForeground(IntPtr handle)
        {
            try
            {
                ShowWindow(handle, SW_RESTORE);

                IntPtr fgWindow = GetForegroundWindow();
                uint fgThread = GetWindowThreadProcessId(fgWindow, out _);
                uint thisThread = GetCurrentThreadId();

                if (fgThread != thisThread)
                {
                    AttachThreadInput(fgThread, thisThread, true);
                    SetForegroundWindow(handle);
                    BringWindowToTop(handle);
                    AttachThreadInput(fgThread, thisThread, false);
                }
                else
                {
                    SetForegroundWindow(handle);
                    BringWindowToTop(handle);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"DIALOG_FOREGROUND_FAILED err=\"{ex.Message}\"");
            }
        }

        private DialogResult ShowDialogOnTop(CommonDialog dlg)
        {
            ForceForeground(_uiThreadMarshal.Handle);
            return dlg.ShowDialog(GetOwner());
        }

        // MC側の既定アップロードURL(後方互換のデフォルト値)。MC運用は今まで通りこのURLを使う。
        private const string DEFAULT_MC_UPLOAD_PATH = "/api/mc/files/upload-by-ticket";

        public PickUploadResponse PickFileAndUpload(string ticket, string fileType = null, string uploadPath = null)
        {
            string[] paths = null;
            _uiThreadMarshal.Invoke(new Action(() =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    if (fileType == "PHOTO")
                    {
                        dlg.Title = "MachCore - 📷 写真ファイルを選択";
                        dlg.Filter = "写真ファイル (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|すべてのファイル (*.*)|*.*";
                        dlg.FilterIndex = 1;
                    }
                    else if (fileType == "DRAWING")
                    {
                        dlg.Title = "MachCore - 📐 図ファイルを選択";
                        dlg.Filter = "図面ファイル (*.tif;*.tiff;*.pdf)|*.tif;*.tiff;*.pdf|すべてのファイル (*.*)|*.*";
                        dlg.FilterIndex = 1;
                    }
                    else if (fileType == "PROGRAM")
                    {
                        dlg.Title = "MachCore - 📄 プログラムファイルを選択";
                        dlg.Filter = "プログラムファイル (*.min;*.spf;*.mpf;*.nc;*.cnc;*.tap;*.prg;*.gcode;*.g;*.txt)|*.min;*.spf;*.mpf;*.nc;*.cnc;*.tap;*.prg;*.gcode;*.g;*.txt|すべてのファイル (*.*)|*.*";
                        dlg.FilterIndex = 2;
                    }
                    else
                    {
                        dlg.Title = "MachCore - アップロードするファイルを選択";
                        dlg.Filter = "すべてのファイル (*.*)|*.*";
                    }
                    dlg.Multiselect = false;
                    var result = ShowDialogOnTop(dlg);
                    if (result == DialogResult.OK) paths = new[] { dlg.FileName };
                }
            }));

            if (paths == null || paths.Length == 0)
            {
                _logger.Info("PICK_UPLOAD_CANCELLED (file)");
                return new PickUploadResponse { cancelled = true, success = false };
            }

            return UploadFiles(ticket, paths, null, null, uploadPath);
        }

        public PickUploadResponse PickFolderAndUpload(string ticket, string fileType = null, string uploadPath = null)
        {
            string folderPath = null;
            _uiThreadMarshal.Invoke(new Action(() =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = fileType == "PHOTO" ? "MachCore - 📷 写真ファイルが入っているフォルダを選択"
                                     : fileType == "DRAWING" ? "MachCore - 📐 図ファイルが入っているフォルダを選択"
                                     : fileType == "PROGRAM" ? "MachCore - 📄 プログラムファイルが入っているフォルダを選択"
                                     : "MachCore - アップロードするフォルダを選択";
                    var result = ShowDialogOnTop(dlg);
                    if (result == DialogResult.OK) folderPath = dlg.SelectedPath;
                }
            }));

            if (string.IsNullOrEmpty(folderPath))
            {
                _logger.Info("PICK_UPLOAD_CANCELLED (folder)");
                return new PickUploadResponse { cancelled = true, success = false };
            }

            // ★選択したフォルダの「元のフォルダ名」(例: "1846.WPD")を保持する。
            //   これをアップロード時にAPIへ送信し、加工IDフォルダの中にこの名前のサブフォルダとして
            //   そのまま保存させる。(加工ID/元フォルダ名/ファイル名 という階層を維持するため)
            string originalFolderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

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

            int totalCount = allFiles.Length;
            allFiles = FilterFilesByType(allFiles, fileType);

            if (totalCount == 0)
                return new PickUploadResponse { cancelled = false, success = false, error = "フォルダ内にファイルがありません" };

            if (allFiles.Length == 0)
            {
                string typeLabel = fileType == "PHOTO" ? "写真(jpg/jpeg/png)"
                                  : fileType == "DRAWING" ? "図面(tif/tiff/pdf)"
                                  : fileType == "PROGRAM" ? "プログラム"
                                  : "対応";
                return new PickUploadResponse { cancelled = false, success = false, error = $"フォルダ内に{typeLabel}ファイルが見つかりません（全{totalCount}件中0件）" };
            }

            // ★PROGRAM（プログラムファイル）の場合: フォルダ単位を選んだ時点で
            //   「フォルダ内の全ファイルをそのままアップロードする」という仕様であり、
            //   1ファイルずつ選別させるグリッド選択UI(FileGridPickerForm)は不要かつ不適切。
            //   写真・図（PHOTO/DRAWING）は複数枚から選んで取り込む運用のためグリッド選択を維持するが、
            //   PROGRAMはここで確認UIを完全にスキップし、フィルタ済み全ファイルを直接アップロードする。
            if (fileType == "PROGRAM")
            {
                _logger.Info($"PROGRAM_FOLDER_DIRECT_UPLOAD count={allFiles.Length} / total={totalCount} folder=\"{folderPath}\" originalFolderName=\"{originalFolderName}\"");
                // ★フォルダ単位アップロード時は、個別ファイルのTrash移動ではなく
                //   全ファイルのアップロードが成功した後にフォルダ全体をTrashへ移動する。
                return UploadFiles(ticket, allFiles, originalFolderName, folderPath, uploadPath);
            }

            string[] selectedFiles = null;
            bool gridCancelled = true;
            _uiThreadMarshal.Invoke(new Action(() =>
            {
                using (var picker = new Forms.FileGridPickerForm(folderPath, allFiles, fileType))
                {
                    ForceForeground(_uiThreadMarshal.Handle);
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

            _logger.Info($"FOLDER_GRID_SELECTED count={selectedFiles.Length} / filtered={allFiles.Length} / total={totalCount}");
            // PHOTO/DRAWINGはグリッドで一部だけ選んだ可能性があるため、フォルダ全体の削除はしない(個別ファイルのみTrash移動)
            return UploadFiles(ticket, selectedFiles, originalFolderName, null, uploadPath);
        }

        private static string[] FilterFilesByType(string[] files, string fileType)
        {
            if (fileType == "PHOTO")
            {
                var exts = new HashSet<string> { ".jpg", ".jpeg", ".png" };
                return files.Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
            }
            if (fileType == "DRAWING")
            {
                var exts = new HashSet<string> { ".tif", ".tiff", ".pdf" };
                return files.Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
            }
            if (fileType == "PROGRAM")
            {
                var exts = new HashSet<string> { ".min", ".spf", ".mpf", ".nc", ".cnc", ".tap", ".prg", ".gcode", ".g", ".txt" };
                return files.Where(f =>
                {
                    var e = Path.GetExtension(f);
                    return e == "" || exts.Contains(e.ToLowerInvariant());
                }).ToArray();
            }
            return files;
        }

        /// <summary>
        /// PG→USB: チケットでAPIからファイル情報(Base64)を取得し、設定済みUSBドライブへ直接コピーする。
        /// ダイアログは一切表示しない。完了後はAPI側へ完了通知を送ってチケットを破棄させる。
        /// </summary>
        public Models.PgToUsbResponse PgToUsb(string ticket, string apiBaseUrl)
        {
            var response = new Models.PgToUsbResponse();
            var usbPath = _settings.GetUsbDrivePathOrNull();
            if (usbPath == null)
            {
                response.success = false;
                response.error = "USB転送先フォルダが設定されていません（設定画面で設定してください）";
                _logger.Warn("PG_TO_USB_NO_DEST_CONFIGURED");
                return response;
            }
            if (!Directory.Exists(usbPath))
            {
                response.success = false;
                response.error = $"USB転送先フォルダが見つかりません: {usbPath}（USBが接続されているか確認してください）";
                _logger.Warn($"PG_TO_USB_DEST_NOT_FOUND path=\"{usbPath}\"");
                return response;
            }

            try
            {
                using (var client = new HttpClient(_sslBypassHandler, disposeHandler: false))
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    var url = $"{apiBaseUrl.TrimEnd('/')}/mc/files/pg-info-by-ticket?ticket={Uri.EscapeDataString(ticket)}";
                    var resp = client.GetAsync(url).GetAwaiter().GetResult();
                    var bodyText = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (!resp.IsSuccessStatusCode)
                    {
                        response.success = false;
                        response.error = $"チケット情報取得失敗 (HTTP {(int)resp.StatusCode}): {bodyText}";
                        _logger.Error($"PG_TO_USB_TICKET_FETCH_FAILED status={(int)resp.StatusCode} body=\"{bodyText}\"");
                        return response;
                    }

                    Dictionary<string, object> info;
                    try { info = _json.Deserialize<Dictionary<string, object>>(bodyText); }
                    catch (Exception ex)
                    {
                        response.success = false;
                        response.error = $"レスポンス解析失敗: {ex.Message}";
                        return response;
                    }

                    if (!info.ContainsKey("files"))
                    {
                        response.success = false;
                        response.error = "ファイル情報が取得できませんでした";
                        return response;
                    }

                    var filesRaw = _json.Serialize(info["files"]);
                    var fileList = _json.Deserialize<List<Dictionary<string, object>>>(filesRaw);

                    if (fileList == null || fileList.Count == 0)
                    {
                        response.success = false;
                        response.error = "プログラムファイルが見つかりません";
                        return response;
                    }

                    string destDir = usbPath;
                    // 複数ファイル（folderName指定あり）の場合はサブフォルダへ
                    string folderName = fileList[0].ContainsKey("folderName") ? fileList[0]["folderName"]?.ToString() : null;
                    if (!string.IsNullOrEmpty(folderName))
                    {
                        destDir = Path.Combine(usbPath, folderName);
                        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    }

                    foreach (var f in fileList)
                    {
                        var name = f["name"]?.ToString();
                        var content = f["content"]?.ToString();
                        if (string.IsNullOrEmpty(name) || content == null) continue;

                        var destPath = Path.Combine(destDir, name);
                        var bytes = Convert.FromBase64String(content);
                        File.WriteAllBytes(destPath, bytes);
                        response.copiedFiles.Add(name);
                        _logger.Info($"PG_TO_USB_COPIED file=\"{name}\" dest=\"{destPath}\"");
                    }

                    response.destPath = destDir;
                    response.success = response.copiedFiles.Count > 0;
                    if (!response.success) response.error = "ファイルのコピーに失敗しました";

                    // 完了通知（チケット破棄）— Web側からも呼ばれるが、Agent側からも確実に送る
                    try
                    {
                        var completeUrl = $"{apiBaseUrl.TrimEnd('/')}/mc/files/pg-to-usb-complete";
                        var completeBody = new StringContent(
                            _json.Serialize(new Dictionary<string, string> { ["ticket"] = ticket }),
                            Encoding.UTF8, "application/json");
                        client.PostAsync(completeUrl, completeBody).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"PG_TO_USB_COMPLETE_NOTIFY_FAILED err=\"{ex.Message}\"");
                    }

                    _stats.IncrementMoved();
                    return response;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"PG_TO_USB_ERROR err=\"{ex.Message}\"");
                response.success = false;
                response.error = ex.Message;
                return response;
            }
        }

        // ★変更: folderName/sourceFolderPath引数を追加。
        //   sourceFolderPathが指定されている場合(=フォルダ単位アップロード)、
        //   全ファイルのアップロードが成功した後にフォルダ全体をTrashへ移動する。
        //   指定がない場合(=単体ファイル、または写真/図のグリッド選択)は、
        //   従来通り各ファイルを個別にTrashへ移動する。
        private PickUploadResponse UploadFiles(string ticket, string[] paths, string folderName, string sourceFolderPath, string uploadPath = null)
        {
            var response = new PickUploadResponse { cancelled = false };
            bool moveWholeFolder = !string.IsNullOrEmpty(sourceFolderPath);

            foreach (var path in paths)
            {
                try
                {
                    // フォルダ全体を後でまとめて移動する場合は、個別ファイルのTrash移動はスキップする
                    var fileResult = UploadSingleFile(ticket, path, folderName, skipLocalTrash: moveWholeFolder, uploadPath: uploadPath);
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

            // ★全ファイルのアップロードが成功し、フォルダ全体移動が指定されている場合のみ、
            //   フォルダごとTrashへ移動する。1件でも失敗していればフォルダは残し、再試行できるようにする。
            if (moveWholeFolder && response.success)
            {
                try
                {
                    var trashDir = _settings.GetEffectiveTrashRoot();
                    if (!Directory.Exists(trashDir)) Directory.CreateDirectory(trashDir);

                    var folderBaseName = Path.GetFileName(sourceFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var destFolderName = $"{folderBaseName}_{timestamp}";
                    var destFolderPath = Path.Combine(trashDir, destFolderName);
                    if (Directory.Exists(destFolderPath))
                    {
                        timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssff");
                        destFolderName = $"{folderBaseName}_{timestamp}";
                        destFolderPath = Path.Combine(trashDir, destFolderName);
                    }

                    Directory.Move(sourceFolderPath, destFolderPath);
                    _logger.Info($"LOCAL_TRASH_FOLDER_OK src=\"{sourceFolderPath}\" dst=\"{destFolderPath}\"");

                    // 各ファイル結果のlocalDeletedフラグも更新しておく(UI表示の整合性のため)
                    foreach (var f in response.files)
                    {
                        f.localDeleted = true;
                        f.localDeleteError = null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"LOCAL_TRASH_FOLDER_FAILED path=\"{sourceFolderPath}\" err=\"{ex.Message}\"");
                    foreach (var f in response.files)
                    {
                        if (string.IsNullOrEmpty(f.localDeleteError))
                            f.localDeleteError = $"フォルダ移動失敗: {ex.Message}";
                    }
                }
            }

            return response;
        }

        // ★変更: folderName/skipLocalTrash/uploadPath引数を追加。
        private UploadedFileResult UploadSingleFile(string ticket, string filePath, string folderName, bool skipLocalTrash = false, string uploadPath = null)
        {
            var fileName = Path.GetFileName(filePath);
            var effectivePath = string.IsNullOrEmpty(uploadPath) ? DEFAULT_MC_UPLOAD_PATH : uploadPath;
            _logger.Info($"UPLOAD_START path=\"{filePath}\" folderName=\"{folderName}\" uploadPath=\"{effectivePath}\"");

            using (var client = new HttpClient(_sslBypassHandler, disposeHandler: false))
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                var url = _settings.MachCoreServerUrl.TrimEnd('/') + effectivePath;

                using (var content = new MultipartFormDataContent())
                using (var fileStream = File.OpenRead(filePath))
                using (var streamContent = new StreamContent(fileStream))
                {
                    // ★修正: 拡張子から正しいMIMEタイプを設定（以前は常にapplication/octet-stream固定 → サーバ側のisImage判定が壊れていた）
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileName));
                    content.Add(new StringContent(ticket), "ticket");
                    content.Add(streamContent, "file", fileName);
                    if (!string.IsNullOrEmpty(folderName))
                    {
                        content.Add(new StringContent(folderName), "folder_name");
                    }

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

                    // ★フォルダ単位アップロードでフォルダ全体を後でまとめて移動する場合は、
                    //   個別ファイルのTrash移動をスキップする(呼び出し元のUploadFilesで一括処理)。
                    if (skipLocalTrash)
                    {
                        return new UploadedFileResult
                        {
                            originalName = fileName,
                            storedName = storedName,
                            fileId = fileId,
                            duplicateHandled = false,
                            localDeleted = false, // フォルダ移動成功後にUploadFiles側でtrueに更新される
                            localDeleteError = null,
                        };
                    }

                    bool localDeleted = false;
                    string localDeleteError = null;
                    try
                    {
                        var trashDir = _settings.GetEffectiveTrashRoot();
                        if (!Directory.Exists(trashDir)) Directory.CreateDirectory(trashDir);

                        var ext = Path.GetExtension(fileName);
                        var nameOnly = Path.GetFileNameWithoutExtension(fileName);
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
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