using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using UploadAgent.Forms;

namespace UploadAgent
{
    static class Program
    {
        private const string APP_NAME       = "MachCore UploadAgent";
        private const string REGISTRY_KEY   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string REGISTRY_VALUE = "MachCoreUploadAgent";

        // ── アプリケーション状態 ──────────────────────────────────
        private static AppSettings       _settings;
        private static AuditLogger       _logger;
        private static StatsCounter      _stats;
        private static SecurityGuard     _guard;
        private static FileOperations    _fileOps;
        private static RouteHandler   　 _router;
        private static UploadCoordinator _uploadCoordinator;
        private static Form            　_hiddenUiForm;
        private static HttpServer        _server;
        private static NotifyIcon        _trayIcon;
        private static string            _token;
        private static bool              _hasError;

        // アイコン（GDI+で動的生成 → .ico ファイル不要）
        private static Icon _iconNormal;
        private static Icon _iconError;

        [STAThread]
        static void Main()
        {
            bool isNew;
            using (new Mutex(true, "MachCoreUploadAgent_SingleInstance", out isNew))
            {
                if (!isNew)
                {
                    MessageBox.Show($"{APP_NAME} は既に起動しています。",
                        APP_NAME, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 設定・ロガー初期化
                _settings = AppSettings.Load();
                // MessageBox.Show($"読み込んだポート: {_settings.Port}\n読み込んだパス: {AppSettings.GetSettingsPathForDebug()}", "DEBUG");
                _logger = new AuditLogger { VerboseEnabled = _settings.VerboseLog };
                _stats    = new StatsCounter();

                // トークン生成・依存オブジェクト構築
                _token   = Guid.NewGuid().ToString();
                _guard   = new SecurityGuard(_token);
                _fileOps = new FileOperations(_logger, _guard, _stats);
                _hiddenUiForm = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, Opacity = 0 };
                var _ = _hiddenUiForm.Handle; // ハンドル生成（Invoke利用のため）
                _uploadCoordinator = new UploadCoordinator(_settings, _logger, _stats, _hiddenUiForm);
                _router = new RouteHandler(_token, _guard, _fileOps, _logger, _settings, _uploadCoordinator);

                // アイコン生成（カスタム指定 or 動的生成）
                BuildIcons();

                // HTTPサーバ起動
                _server = new HttpServer(_settings.Port, _router, _logger);
                try
                {
                    _server.Start();
                    _hasError = false;
                    _logger.Info($"AGENT_STARTED port={_settings.Port}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"SERVER_START_FAILED err=\"{ex.Message}\"");
                    _hasError = true;
                    MessageBox.Show(
                        $"HTTPサーバの起動に失敗しました。\nポート {_settings.Port} が使用中の可能性があります。\n\n{ex.Message}",
                        APP_NAME, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                // 自動起動設定
                ApplyAutoStartRegistry();

                // システムトレイ設定
                SetupTrayIcon();

                Application.Run();

                // 終了処理
                _server?.Dispose();
                _trayIcon?.Dispose();
                _iconNormal?.Dispose();
                _iconError?.Dispose();
                _logger.Info("AGENT_STOPPED");
            }
        }

        // ════════════════════════════════════════════════════════
        // アイコン
        // ════════════════════════════════════════════════════════
        private static void BuildIcons()
        {
            // カスタム .ico が指定されていれば優先使用
            var custom = _settings.LoadCustomIcon();
            if (custom != null) { _iconNormal = custom; }
            else                { _iconNormal = CreateDynamicIcon(Color.FromArgb(0, 160, 100), "MC"); }

            _iconError = CreateDynamicIcon(Color.FromArgb(196, 43, 28), "MC");
        }

        /// <summary>
        /// .ico ファイル不要で GDI+ から動的に Icon を生成する
        /// </summary>
        private static Icon CreateDynamicIcon(Color bgColor, string text)
        {
            const int size = 32;
            using (var bmp = new Bitmap(size, size))
            using (var g   = Graphics.FromImage(bmp))
            {
                g.SmoothingMode   = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // 背景丸四角
                using (var br = new SolidBrush(bgColor))
                    g.FillRoundedRectangle(br, new Rectangle(1, 1, size - 2, size - 2), 6);

                // テキスト
                using (var font = new Font("Arial", 10f, FontStyle.Bold))
                using (var sf   = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var br   = new SolidBrush(Color.White))
                    g.DrawString(text, font, br, new RectangleF(0, 0, size, size), sf);

                // Bitmap → Icon 変換（IntPtr 経由）
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        /// <summary>トレイアイコンをエラー/正常状態で切り替える</summary>
        private static void SetTrayIconState(bool isError)
        {
            if (_trayIcon == null) return;
            _hasError       = isError;
            _trayIcon.Icon  = isError ? _iconError : _iconNormal;
            _trayIcon.Text  = isError
                ? $"{APP_NAME}\n⚠ エラー状態"
                : $"{APP_NAME}\nポート: {_settings.Port}";
        }

        // ════════════════════════════════════════════════════════
        // システムトレイ
        // ════════════════════════════════════════════════════════
        private static void SetupTrayIcon()
        {
            var menu = BuildContextMenu();

            _trayIcon = new NotifyIcon
            {
                Icon        = _hasError ? _iconError : _iconNormal,
                Text        = $"{APP_NAME}\nポート: {_settings.Port}",
                ContextMenu = menu,
                Visible     = true,
            };
            _trayIcon.DoubleClick += (s, e) => OpenSettings();

            if (_settings.ShowBalloonNotify && !_hasError)
            {
                _trayIcon.ShowBalloonTip(3000, APP_NAME,
                    $"起動しました（ポート: {_settings.Port}）\nMachCoreからのUSB削除依頼を受け付けます。",
                    ToolTipIcon.Info);
            }
        }

        private static ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();

            // ── ヘッダー（クリック不可）
            var header = new MenuItem($"{APP_NAME}  ( :{_settings.Port} )") { Enabled = false };
            menu.MenuItems.Add(header);
            menu.MenuItems.Add(new MenuItem("-"));

            // ── 設定
            var miSettings = new MenuItem("⚙ 設定...", (s, e) => OpenSettings());
            menu.MenuItems.Add(miSettings);
            menu.MenuItems.Add(new MenuItem("-"));

            // ── ログ
            var miLog = new MenuItem("📋 ログフォルダを開く", (s, e) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", AuditLogger.LogDir); } catch { }
            });
            menu.MenuItems.Add(miLog);

            // ── 統計（動的更新は ShowContextMenu 時に行う）
            var miStats = new MenuItem("📊 統計情報を表示", (s, e) => ShowStatsDialog());
            menu.MenuItems.Add(miStats);

            menu.MenuItems.Add(new MenuItem("-"));

            // ── 再起動
            var miRestart = new MenuItem("🔄 再起動", (s, e) =>
            {
                var ans = MessageBox.Show(
                    "UploadAgentを再起動しますか？\n（一時的に接続が切れます）",
                    APP_NAME, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ans != DialogResult.Yes) return;

                _logger.Info("AGENT_RESTART_REQUESTED");
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                _trayIcon.Visible = false;
                _server?.Dispose();

                // 別スレッドで待機してから起動（Mutexのusing解放を待つ）
                System.Threading.Tasks.Task.Run(() => {
                    System.Threading.Thread.Sleep(1500);
                    System.Diagnostics.Process.Start(exePath);
                });
                Application.Exit();
            });
            menu.MenuItems.Add(miRestart);
            menu.MenuItems.Add(new MenuItem("-"));

            // ── 終了
            var miExit = new MenuItem("終了", (s, e) => ExitApp());
            menu.MenuItems.Add(miExit);

            return menu;
        }

        private static void ShowStatsDialog()
        {
            var (moved, error) = _stats.GetToday();
            MessageBox.Show(
                $"本日の処理件数\n\n" +
                $"  ファイル移動: {moved} 件\n" +
                $"  エラー:       {error} 件\n\n" +
                $"待受ポート: {_settings.Port}\n" +
                $"MachCoreサーバ: {_settings.MachCoreServerUrl}",
                $"{APP_NAME} - 統計", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ════════════════════════════════════════════════════════
        // 設定画面
        // ════════════════════════════════════════════════════════
        private static void OpenSettings()
        {
            var form = new SettingsForm(_settings, _logger, _stats, _fileOps, _settings.Port,
                onSaved: (newSettings) =>
                {
                    // 詳細ログ即時反映
                    _logger.VerboseEnabled = newSettings.VerboseLog;

                    // アイコン更新
                    BuildIcons();
                    SetTrayIconState(_hasError);

                    // 自動起動レジストリ更新
                    ApplyAutoStartRegistry();

                    // ポートが変更された場合は再起動
                    if (newSettings.Port != _settings.Port)
                    {
                        _logger.Info($"PORT_CHANGED {_settings.Port} → {newSettings.Port} RESTARTING");
                        RestartServer(newSettings.Port);
                    }

                    if (newSettings.ShowBalloonNotify)
                    {
                        _trayIcon?.ShowBalloonTip(2000, APP_NAME,
                            "設定を保存しました。", ToolTipIcon.Info);
                    }
                });

            form.Show();
        }

        // ════════════════════════════════════════════════════════
        // サーバ再起動（ポート変更時）
        // ════════════════════════════════════════════════════════
        private static void RestartServer(int newPort)
        {
            try
            {
                _server?.Dispose();
                _server = new HttpServer(newPort, _router, _logger);
                _server.Start();
                _settings.Port = newPort;
                SetTrayIconState(false);
                _trayIcon.Text = $"{APP_NAME}\nポート: {newPort}";
                _logger.Info($"SERVER_RESTARTED port={newPort}");
            }
            catch (Exception ex)
            {
                _logger.Error($"SERVER_RESTART_FAILED err=\"{ex.Message}\"");
                SetTrayIconState(true);
                MessageBox.Show($"サーバの再起動に失敗しました。\n{ex.Message}", APP_NAME,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ════════════════════════════════════════════════════════
        // 自動起動レジストリ
        // ════════════════════════════════════════════════════════
        private static void ApplyAutoStartRegistry()
        {
            try
            {
                var exePath = Assembly.GetExecutingAssembly().Location;
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, true))
                {
                    if (key == null) return;
                    if (_settings.AutoStart)
                    {
                        var existing = key.GetValue(REGISTRY_VALUE) as string;
                        if (existing != exePath)
                        {
                            key.SetValue(REGISTRY_VALUE, exePath);
                            _logger.Info($"STARTUP_REGISTERED path=\"{exePath}\"");
                        }
                    }
                    else
                    {
                        if (key.GetValue(REGISTRY_VALUE) != null)
                        {
                            key.DeleteValue(REGISTRY_VALUE);
                            _logger.Info("STARTUP_UNREGISTERED");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"STARTUP_REGISTRY_FAILED err=\"{ex.Message}\"");
            }
        }

        // ════════════════════════════════════════════════════════
        // 終了
        // ════════════════════════════════════════════════════════
        private static void ExitApp()
        {
            var result = MessageBox.Show(
                $"{APP_NAME} を終了しますか？\n終了するとMachCoreからのUSB削除依頼が処理されなくなります。",
                APP_NAME, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;
            _trayIcon.Visible = false;
            Application.Exit();
        }
    }

    // ════════════════════════════════════════════════════════════
    // Graphics 拡張メソッド（丸四角描画）
    // ════════════════════════════════════════════════════════════
    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(rect.Left,              rect.Top,             d, d, 180, 90);
                path.AddArc(rect.Right - d,         rect.Top,             d, d, 270, 90);
                path.AddArc(rect.Right - d,         rect.Bottom - d,      d, d,   0, 90);
                path.AddArc(rect.Left,              rect.Bottom - d,      d, d,  90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }
    }
}
