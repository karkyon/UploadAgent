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
        private readonly string            _token;
        private readonly SecurityGuard     _guard;
        private readonly FileOperations    _fileOps;
        private readonly AuditLogger       _logger;
        private readonly AppSettings       _settings;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public RouteHandler(string token, SecurityGuard guard, FileOperations fileOps,
                            AuditLogger logger, AppSettings settings)
        {
            _token    = token;
            _guard    = guard;
            _fileOps  = fileOps;
            _logger   = logger;
            _settings = settings;
        }

        public bool Handle(HttpListenerContext ctx)
        {
            var req    = ctx.Request;
            var path   = req.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            var method = req.HttpMethod.ToUpperInvariant();

            ctx.Response.Headers.Add("Access-Control-Allow-Origin",  "*");
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Agent-Token");

            if (method == "OPTIONS") { ctx.Response.StatusCode = 204; ctx.Response.Close(); return true; }

            _logger.Verbose($"REQUEST {method} {path}");

            switch (path)
            {
                case "/health"  when method == "GET":  HandleHealth(ctx);  return true;
                case "/move"    when method == "POST": HandleMove(ctx);    return true;
                case "/delete"  when method == "POST": HandleMove(ctx);    return true; // 後方互換
                case "/drives"  when method == "GET":  HandleDrives(ctx);  return true;
                default: return false;
            }
        }

        // ── GET /health ──────────────────────────────────────────
        private void HandleHealth(HttpListenerContext ctx)
        {
            SendJson(ctx, 200, new HealthResponse { token = _token });
            _logger.Verbose("HEALTH_CHECK ok");
        }

        // ── POST /move, /delete ──────────────────────────────────
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
                moveReq  = _json.Deserialize<MoveRequest>(body);
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

            var result     = _fileOps.MoveFiles(moveReq);
            int statusCode = result.success ? 200 : (result.moved.Count > 0 ? 207 : 400);
            SendJson(ctx, statusCode, result);
        }

        // ── GET /drives ──────────────────────────────────────────
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

        // ── ヘルパー ─────────────────────────────────────────────
        private void SendJson(HttpListenerContext ctx, int statusCode, object obj)
        {
            var bytes = Encoding.UTF8.GetBytes(_json.Serialize(obj));
            ctx.Response.StatusCode      = statusCode;
            ctx.Response.ContentType     = "application/json; charset=utf-8";
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
