using System;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using UploadAgent.Models;

namespace UploadAgent
{
    public class RouteHandler
    {
        private readonly string _token;
        private readonly SecurityGuard _guard;
        private readonly FileOperations _fileOps;
        private readonly AuditLogger _logger;
        private readonly AppSettings _settings;
        private readonly UploadCoordinator _uploadCoordinator;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public RouteHandler(string token, SecurityGuard guard, FileOperations fileOps,
                            AuditLogger logger, AppSettings settings, UploadCoordinator uploadCoordinator)
        {
            _token = token;
            _guard = guard;
            _fileOps = fileOps;
            _logger = logger;
            _settings = settings;
            _uploadCoordinator = uploadCoordinator;
        }

        // MachCore正規オリジン（appsettings.json の MachCoreServerUrl から導出）
        private string ExpectedOrigin => _settings.MachCoreServerUrl?.TrimEnd('/');

        public bool Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var path = req.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            var method = req.HttpMethod.ToUpperInvariant();

            // CORS: 正規オリジンのみ許可（ワイルドカード禁止 - ClawJacked対策）
            var origin = req.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && _guard.IsAllowedOrigin(origin, ExpectedOrigin))
            {
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", origin);
            }
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Agent-Token");

            if (method == "OPTIONS") { ctx.Response.StatusCode = 204; ctx.Response.Close(); return true; }

            // 操作系エンドポイント（move/delete/pick系）はOrigin検証必須
            bool isStateChanging = (path == "/move" || path == "/delete" || path == "/pick-and-upload" || path == "/pick-folder-and-upload");
            if (isStateChanging && !_guard.IsAllowedOrigin(origin, ExpectedOrigin))
            {
                _logger.Warn($"ORIGIN_REJECTED path=\"{path}\" origin=\"{origin}\" expected=\"{ExpectedOrigin}\"");
                SendJson(ctx, 403, new { error = "Forbidden: invalid origin" });
                return true;
            }

            _logger.Verbose($"REQUEST {method} {path} origin=\"{origin}\"");

            switch (path)
            {
                case "/health" when method == "GET": HandleHealth(ctx); return true;
                case "/move" when method == "POST": HandleMove(ctx); return true;
                case "/delete" when method == "POST": HandleMove(ctx); return true;
                case "/drives" when method == "GET": HandleDrives(ctx); return true;
                case "/pick-and-upload" when method == "POST": HandlePickAndUpload(ctx, isFolder: false); return true;
                case "/pick-folder-and-upload" when method == "POST": HandlePickAndUpload(ctx, isFolder: true); return true;
                default: return false;
            }
        }

        private void HandleHealth(HttpListenerContext ctx)
        {
            SendJson(ctx, 200, new HealthResponse { token = _token });
            _logger.Verbose("HEALTH_CHECK ok");
        }

        private void HandleMove(HttpListenerContext ctx)
        {
            var reqToken = ctx.Request.Headers["X-Agent-Token"] ?? "";
            if (!_guard.ValidateToken(reqToken))
            {
                _logger.Warn($"AUTH_FAIL reason=token_mismatch remote={ctx.Request.RemoteEndPoint}");
                SendJson(ctx, 401, new { error = "Unauthorized: invalid token" });
                return;
            }

            MoveRequest moveReq;
            try
            {
                var body = ReadBody(ctx.Request);
                moveReq = _json.Deserialize<MoveRequest>(body);
            }
            catch (Exception ex)
            {
                SendJson(ctx, 400, new { error = $"Invalid JSON: {ex.Message}" });
                return;
            }

            if (moveReq == null || moveReq.paths == null || moveReq.paths.Count == 0)
            {
                SendJson(ctx, 400, new { error = "paths is required and must not be empty" });
                return;
            }

            var result = _fileOps.MoveFiles(moveReq);
            int statusCode = result.success ? 200 : (result.moved.Count > 0 ? 207 : 400);
            SendJson(ctx, statusCode, result);
        }

        private void HandleDrives(HttpListenerContext ctx)
        {
            var reqToken = ctx.Request.Headers["X-Agent-Token"] ?? "";
            if (!_guard.ValidateToken(reqToken))
            {
                SendJson(ctx, 401, new { error = "Unauthorized: invalid token" });
                return;
            }
            SendJson(ctx, 200, _fileOps.GetRemovableDrives());
        }

        // ── POST /pick-and-upload, /pick-folder-and-upload ────────
        // Web は Bearerトークンを一切渡さず、ワンタイムチケットのみを渡す。
        // Agent はここでダイアログ表示〜アップロード〜削除までを一括実行する。
        private void HandlePickAndUpload(HttpListenerContext ctx, bool isFolder)
        {
            var reqToken = ctx.Request.Headers["X-Agent-Token"] ?? "";
            if (!_guard.ValidateToken(reqToken))
            {
                _logger.Warn($"AUTH_FAIL reason=token_mismatch remote={ctx.Request.RemoteEndPoint}");
                SendJson(ctx, 401, new { error = "Unauthorized: invalid token" });
                return;
            }

            PickUploadRequest pickReq;
            try
            {
                var body = ReadBody(ctx.Request);
                pickReq = _json.Deserialize<PickUploadRequest>(body);
            }
            catch (Exception ex)
            {
                SendJson(ctx, 400, new { error = $"Invalid JSON: {ex.Message}" });
                return;
            }

            if (pickReq == null || string.IsNullOrWhiteSpace(pickReq.ticket))
            {
                SendJson(ctx, 400, new { error = "ticket is required" });
                return;
            }

            _logger.Info($"PICK_AND_UPLOAD_REQUEST isFolder={isFolder}");

            var result = isFolder
                ? _uploadCoordinator.PickFolderAndUpload(pickReq.ticket, pickReq.fileType)
                : _uploadCoordinator.PickFileAndUpload(pickReq.ticket, pickReq.fileType);

            SendJson(ctx, 200, result);
        }

        private void SendJson(HttpListenerContext ctx, int statusCode, object obj)
        {
            var bytes = Encoding.UTF8.GetBytes(_json.Serialize(obj));
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            try { ctx.Response.OutputStream.Write(bytes, 0, bytes.Length); }
            finally { ctx.Response.Close(); }
        }

        private string ReadBody(HttpListenerRequest req)
        {
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}