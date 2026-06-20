using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UploadAgent
{
    /// <summary>
    /// appsettings.json への読み書きを管理する設定クラス
    /// 保存先: %APPDATA%\MachCore\UploadAgent\appsettings.json
    /// </summary>
    public class AppSettings
    {
        // ── デフォルト値 ─────────────────────────────────────────
        public int    Port               { get; set; } = 57300;
        public string MachCoreServerUrl  { get; set; } = "https://192.168.1.11:8443";
        public bool   AutoStart          { get; set; } = true;
        public bool   ShowBalloonNotify  { get; set; } = true;
        public bool   VerboseLog         { get; set; } = false;
        public string CustomIconPath     { get; set; } = "";

        // ── 静的ファクトリ ────────────────────────────────────────
        private static readonly string _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MachCore", "UploadAgent", "appsettings.json"
        );

        public static string GetSettingsPathForDebug() => _settingsPath;

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return new AppSettings();
                var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                var ser  = new JavaScriptSerializer();
                var dict = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                var s    = new AppSettings();
                if (dict.TryGetValue("port",              out var p))   s.Port              = Convert.ToInt32(p);
                if (dict.TryGetValue("machCoreServerUrl", out var u))   s.MachCoreServerUrl = u?.ToString() ?? s.MachCoreServerUrl;
                if (dict.TryGetValue("autoStart",         out var a))   s.AutoStart         = Convert.ToBoolean(a);
                if (dict.TryGetValue("showBalloonNotify", out var b))   s.ShowBalloonNotify = Convert.ToBoolean(b);
                if (dict.TryGetValue("verboseLog",        out var v))   s.VerboseLog        = Convert.ToBoolean(v);
                if (dict.TryGetValue("customIconPath",    out var ic))  s.CustomIconPath    = ic?.ToString() ?? "";
                return s;
            }
            catch { return new AppSettings(); }
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var ser  = new JavaScriptSerializer();
            var dict = new System.Collections.Generic.Dictionary<string, object>
            {
                ["port"]              = Port,
                ["machCoreServerUrl"] = MachCoreServerUrl,
                ["autoStart"]         = AutoStart,
                ["showBalloonNotify"] = ShowBalloonNotify,
                ["verboseLog"]        = VerboseLog,
                ["customIconPath"]    = CustomIconPath,
            };
            File.WriteAllText(_settingsPath, ser.Serialize(dict), Encoding.UTF8);
        }

        /// <summary>カスタムアイコンを読み込む。失敗したら null を返す</summary>
        public Icon LoadCustomIcon()
        {
            if (string.IsNullOrWhiteSpace(CustomIconPath)) return null;
            try
            {
                if (File.Exists(CustomIconPath)) return new Icon(CustomIconPath);
            }
            catch { }
            return null;
        }
    }
}
