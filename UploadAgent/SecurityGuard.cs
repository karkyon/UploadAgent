using System;
using System.IO;
using System.Linq;

namespace UploadAgent
{
    public class SecurityGuard
    {
        private readonly string _expectedToken;

        public SecurityGuard(string token) { _expectedToken = token; }

        public bool ValidateToken(string requestToken)
        {
            if (string.IsNullOrEmpty(requestToken)) return false;
            return SlowEquals(requestToken, _expectedToken);
        }

        public bool IsAllowedPath(string path, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(path)) { reason = "path is empty"; return false; }

            string normalized;
            try { normalized = Path.GetFullPath(path); }
            catch (Exception ex) { reason = $"path normalization failed: {ex.Message}"; return false; }

            if (normalized.IndexOf(".machcore_trash", StringComparison.OrdinalIgnoreCase) >= 0)
            { reason = "operation on .machcore_trash is not allowed"; return false; }

            var drive = DriveInfo.GetDrives().FirstOrDefault(d =>
                normalized.StartsWith(d.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase));

            if (drive == null) { reason = $"drive not found: {normalized}"; return false; }
            if (drive.DriveType != DriveType.Removable) { reason = $"not removable: {drive.Name}"; return false; }
            return true;
        }

        // ── Origin検証（ClawJacked対策） ─────────────────────────
        // MachCore正規オリジン以外からのリクエストを拒否する。
        public bool IsAllowedOrigin(string origin, string expectedOrigin)
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            if (string.IsNullOrWhiteSpace(expectedOrigin)) return false;
            return string.Equals(origin.TrimEnd('/'), expectedOrigin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SlowEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            int diff = a.Length ^ b.Length;
            int len = Math.Max(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                char ca = i < a.Length ? a[i] : '\0';
                char cb = i < b.Length ? b[i] : '\0';
                diff |= ca ^ cb;
            }
            return diff == 0;
        }
    }
}