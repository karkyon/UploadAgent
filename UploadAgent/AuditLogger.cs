using System;
using System.IO;
using System.Text;

namespace UploadAgent
{
    public class AuditLogger : IDisposable
    {
        private const long MaxFileSizeBytes = 10 * 1024 * 1024;
        private const int  MaxGenerations   = 5;

        private readonly string _logPath;
        private readonly string _errorLogPath;
        private readonly object _lock = new object();

        public bool VerboseEnabled { get; set; } = false;

        public static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MachCore", "UploadAgent"
        );

        public AuditLogger()
        {
            Directory.CreateDirectory(LogDir);
            _logPath      = Path.Combine(LogDir, "agent.log");
            _errorLogPath = Path.Combine(LogDir, "agent_error.log");
        }

        public void Info(string message)                   => Write(_logPath,      "INFO ", message);
        public void Warn(string message)                   => Write(_logPath,      "WARN ", message);
        public void Error(string message)                  => Write(_errorLogPath, "ERROR", message);
        public void Verbose(string message)
        {
            if (VerboseEnabled) Write(_logPath, "DEBUG", message);
        }

        /// <summary>最新ログ行を末尾から取得（ログビューア用）</summary>
        public string[] GetRecentLines(int count = 200)
        {
            try
            {
                if (!File.Exists(_logPath)) return Array.Empty<string>();
                var all   = File.ReadAllLines(_logPath, Encoding.UTF8);
                var start = Math.Max(0, all.Length - count);
                var result = new string[all.Length - start];
                Array.Copy(all, start, result, 0, result.Length);
                // 新しい順に並べ替え
                Array.Reverse(result);
                return result;
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>ログファイルの合計サイズ（Trash含まない）</summary>
        public long GetLogSizeBytes()
        {
            long total = 0;
            try
            {
                foreach (var f in Directory.GetFiles(LogDir, "agent*.log*"))
                    total += new FileInfo(f).Length;
            }
            catch { }
            return total;
        }

        private void Write(string path, string level, string message)
        {
            lock (_lock)
            {
                try
                {
                    RotateIfNeeded(path);
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                catch { }
            }
        }

        private void RotateIfNeeded(string path)
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length < MaxFileSizeBytes) return;
            for (int i = MaxGenerations - 1; i >= 1; i--)
            {
                var older = $"{path}.{i}";
                var newer = $"{path}.{i + 1}";
                if (File.Exists(older))
                {
                    if (File.Exists(newer)) File.Delete(newer);
                    File.Move(older, newer);
                }
            }
            File.Move(path, $"{path}.1");
        }

        public void Dispose() { }
    }
}
