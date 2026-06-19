using System;
using System.Net;
using System.Threading;

namespace UploadAgent
{
    public class HttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly RouteHandler _router;
        private readonly AuditLogger  _logger;
        private          Thread       _thread;
        private volatile bool         _running;

        public HttpServer(int port, RouteHandler router, AuditLogger logger)
        {
            _router   = router;
            _logger   = logger;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
        }

        public void Start()
        {
            _listener.Start();
            _running = true;
            _thread  = new Thread(ListenLoop) { IsBackground = true, Name = "HttpServer" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
        }

        private void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx = null;
                try { ctx = _listener.GetContext(); }
                catch (HttpListenerException) { break; }
                catch (Exception ex) { _logger.Error($"LISTEN_ERROR err=\"{ex.Message}\""); continue; }
                ThreadPool.QueueUserWorkItem(_ => ProcessRequest(ctx));
            }
        }

        private void ProcessRequest(HttpListenerContext ctx)
        {
            try
            {
                if (!_router.Handle(ctx))
                { ctx.Response.StatusCode = 404; ctx.Response.Close(); }
            }
            catch (Exception ex)
            {
                _logger.Error($"REQUEST_ERROR path=\"{ctx?.Request?.Url?.AbsolutePath}\" err=\"{ex.Message}\"");
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        public void Dispose() { Stop(); try { _listener.Close(); } catch { } }
    }
}
