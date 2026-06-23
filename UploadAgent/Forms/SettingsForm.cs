using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace UploadAgent.Forms
{
    /// <summary>
    /// 設定・ステータス・ログビューア・Trash管理を統合した設定フォーム
    /// </summary>
    public class SettingsForm : Form
    {
        // ── 依存オブジェクト ──────────────────────────────────────
        private readonly AppSettings   _settings;
        private readonly AuditLogger   _logger;
        private readonly StatsCounter  _stats;
        private readonly FileOperations _fileOps;
        private readonly int           _currentPort;
        private readonly Action<AppSettings> _onSaved; // 保存後コールバック

        // ── タブ・コントロール ────────────────────────────────────
        private TabControl   _tab;
        // [接続設定]
        private NumericUpDown _numPort;
        private TextBox       _txtServerUrl;
        private CheckBox      _chkAutoStart;
        private CheckBox      _chkBalloon;
        private CheckBox      _chkVerbose;
        private TextBox       _txtIconPath;
        private Button        _btnIconBrowse;
        private Button        _btnIconClear;
        // [ステータス]
        private Label  _lblStatus;
        private Label  _lblMoved;
        private Label  _lblError;
        private Label  _lblPort;
        private Label  _lblPing;
        private Button _btnPing;
        private System.Windows.Forms.Timer _statsTimer;
        // [ログビューア]
        private RichTextBox _rtbLog;
        private Button      _btnRefreshLog;
        private Button      _btnOpenLogDir;
        private Label       _lblLogSize;
        // [Trash管理]
        private Label  _lblTrashSize;
        private Button _btnRefreshTrash;
        private Button _btnClearTrash;
        private ListBox _lbTrashFiles;
        private TextBox _txtTrashFolder;
        private Button _btnTrashFolderBrowse;
        private Button _btnOpenTrashFolder; 
        private TextBox _txtUsbDrivePath;
        private Button _btnUsbDriveBrowse;

        public SettingsForm(AppSettings settings, AuditLogger logger,
                            StatsCounter stats, FileOperations fileOps,
                            int currentPort, Action<AppSettings> onSaved)
        {
            _settings    = settings;
            _logger      = logger;
            _stats       = stats;
            _fileOps     = fileOps;
            _currentPort = currentPort;
            _onSaved     = onSaved;

            InitializeComponent();
            LoadValues();
            this.Load += (s, e) => { RefreshStats(); RefreshLog(); RefreshTrash(); };
        }

        // ════════════════════════════════════════════════════════
        // UI 構築
        // ════════════════════════════════════════════════════════
        private void InitializeComponent()
        {
            this.Text            = "MachCore UploadAgent - 設定";
            this.Size            = new Size(680, 560);
            this.MinimumSize     = new Size(680, 560);
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox     = false;

            _tab = new TabControl { Dock = DockStyle.Fill };
            this.Controls.Add(_tab);

            BuildTabConnection();
            BuildTabStatus();
            BuildTabLog();
            BuildTabTrash();

            // 下部ボタン
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            var btnSave   = new Button { Text = "保存して再起動", Width = 140, Height = 34, Left = this.ClientSize.Width - 310, Top = 7, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            var btnCancel = new Button { Text = "キャンセル",     Width = 100, Height = 34, Left = this.ClientSize.Width - 160, Top = 7, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            btnSave.Click   += BtnSave_Click;
            btnCancel.Click += (s, e) => this.Close();
            pnlBottom.Controls.AddRange(new Control[] { btnSave, btnCancel });
            this.Controls.Add(pnlBottom);

            // 統計タイマー（5秒ごとに更新）
            _statsTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _statsTimer.Tick += (s, e) => RefreshStats();
            _statsTimer.Start();
            this.FormClosed += (s, e) => _statsTimer.Stop();
        }

        // ── タブ①: 接続設定 ──────────────────────────────────────
        private void BuildTabConnection()
        {
            var tab = new TabPage("⚙ 設定");
            _tab.TabPages.Add(tab);

            int y = 20; int lx = 20; int cx = 180;

            // ポート
            AddLabel(tab, "ポート番号:", lx, y);
            _numPort = new NumericUpDown { Left = cx, Top = y - 3, Width = 100, Minimum = 1024, Maximum = 65535 };
            tab.Controls.Add(_numPort);
            AddLabel(tab, "（変更後は再起動が必要）", cx + 110, y, Color.Gray);

            y += 40;
            // MachCoreサーバURL
            AddLabel(tab, "MachCoreサーバURL:", lx, y);
            _txtServerUrl = new TextBox { Left = cx, Top = y - 2, Width = 420 };
            tab.Controls.Add(_txtServerUrl);

            y += 40;
            // 自動起動
            _chkAutoStart = new CheckBox { Text = "Windowsログイン時に自動起動する", Left = lx, Top = y, Width = 350 };
            tab.Controls.Add(_chkAutoStart);

            y += 30;
            // バルーン通知
            _chkBalloon = new CheckBox { Text = "操作完了時にバルーン通知を表示する", Left = lx, Top = y, Width = 350 };
            tab.Controls.Add(_chkBalloon);

            y += 30;
            // 詳細ログ
            _chkVerbose = new CheckBox { Text = "詳細ログを有効にする（デバッグ用）", Left = lx, Top = y, Width = 350 };
            tab.Controls.Add(_chkVerbose);

            y += 50;
            // カスタムアイコン
            AddLabel(tab, "カスタムアイコン (.ico):", lx, y);
            _txtIconPath   = new TextBox { Left = cx, Top = y - 2, Width = 300, ReadOnly = true, BackColor = SystemColors.Window };
            _btnIconBrowse = new Button  { Text = "参照...", Left = cx + 308, Top = y - 3, Width = 60, Height = 24 };
            _btnIconClear  = new Button  { Text = "クリア",  Left = cx + 376, Top = y - 3, Width = 50, Height = 24 };
            _btnIconBrowse.Click += BtnIconBrowse_Click;
            _btnIconClear.Click  += (s, e) => _txtIconPath.Text = "";
            tab.Controls.AddRange(new Control[] { _txtIconPath, _btnIconBrowse, _btnIconClear });

            y += 20;
            AddLabel(tab, "※ アイコンを省略するとデフォルトアイコンを使用します", lx, y + 10, Color.Gray);

            y += 40;
            var sep2 = new Label { Text = "─────────── PG→USB / 写真・図取込 既定ドライブ ───────────", Left = lx, Top = y, Width = 500, ForeColor = Color.Gray };
            tab.Controls.Add(sep2);

            y += 30;
            AddLabel(tab, "USB転送先フォルダ:", lx, y);
            _txtUsbDrivePath = new TextBox { Left = cx, Top = y - 2, Width = 300, ReadOnly = true, BackColor = SystemColors.Window };
            _btnUsbDriveBrowse = new Button { Text = "参照...", Left = cx + 308, Top = y - 3, Width = 60, Height = 24 };
            _btnUsbDriveBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "PG→USB転送先・写真/図取込の既定ドライブを選択", SelectedPath = _txtUsbDrivePath.Text })
                {
                    if (dlg.ShowDialog() == DialogResult.OK) _txtUsbDrivePath.Text = dlg.SelectedPath;
                }
            };
            tab.Controls.AddRange(new Control[] { _txtUsbDrivePath, _btnUsbDriveBrowse });

            y += 20;
            AddLabel(tab, "※ MC詳細画面の「PG→USB」ボタンの転送先。未設定の場合PG→USBは使用できません", lx, y + 10, Color.Gray);
        }

        // ── タブ②: ステータス ────────────────────────────────────
        private void BuildTabStatus()
        {
            var tab = new TabPage("📊 ステータス");
            _tab.TabPages.Add(tab);

            int y = 20; int lx = 20; int vx = 200;

            AddLabel(tab, "稼働状態:", lx, y, bold: true);
            _lblStatus = AddValueLabel(tab, "確認中...", vx, y, Color.Gray);

            y += 35;
            AddLabel(tab, "待受ポート:", lx, y, bold: true);
            _lblPort = AddValueLabel(tab, $"{_currentPort}", vx, y);

            y += 35;
            AddLabel(tab, "本日 移動件数:", lx, y, bold: true);
            _lblMoved = AddValueLabel(tab, "0 件", vx, y, Color.Green);

            y += 35;
            AddLabel(tab, "本日 エラー件数:", lx, y, bold: true);
            _lblError = AddValueLabel(tab, "0 件", vx, y, Color.Red);

            y += 50;
            var sep = new Label { Text = "─────────── MachCoreサーバ疎通確認 ───────────", Left = lx, Top = y, Width = 500, ForeColor = Color.Gray };
            tab.Controls.Add(sep);

            y += 25;
            AddLabel(tab, "サーバ応答:", lx, y, bold: true);
            _lblPing = AddValueLabel(tab, "未確認", vx, y, Color.Gray);
            _btnPing = new Button { Text = "疎通確認", Left = vx + 160, Top = y - 3, Width = 90, Height = 26 };
            _btnPing.Click += BtnPing_Click;
            tab.Controls.Add(_btnPing);
        }

        // ── タブ③: ログビューア ──────────────────────────────────
        private void BuildTabLog()
        {
            var tab = new TabPage("📋 ログ");
            _tab.TabPages.Add(tab);

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 36 };
            _btnRefreshLog = new Button { Text = "更新",           Left = 8,   Top = 6, Width = 70, Height = 26 };
            _btnOpenLogDir = new Button { Text = "フォルダを開く", Left = 86,  Top = 6, Width = 110, Height = 26 };
            _lblLogSize    = new Label  { Text = "",               Left = 210, Top = 10, Width = 300, ForeColor = Color.Gray };
            _btnRefreshLog.Click += (s, e) => RefreshLog();
            _btnOpenLogDir.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", AuditLogger.LogDir); } catch { } };
            pnlTop.Controls.AddRange(new Control[] { _btnRefreshLog, _btnOpenLogDir, _lblLogSize });
            tab.Controls.Add(pnlTop);

            _rtbLog = new RichTextBox
            {
                Dock      = DockStyle.Fill,
                ReadOnly  = true,
                Font      = new Font("Consolas", 9f),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.LightGreen,
                BorderStyle = BorderStyle.None,
                ScrollBars  = RichTextBoxScrollBars.Vertical,
            };
            tab.Controls.Add(_rtbLog);
        }

        // ── タブ④: Trash管理 ────────────────────────────────────
        private void BuildTabTrash()
        {
            var tab = new TabPage("🗑 Trash管理");
            _tab.TabPages.Add(tab);

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 36 };
            _lblTrashSize = new Label { Text = "計算中...", Left = 8, Top = 10, Width = 300, ForeColor = Color.DimGray };
            _btnRefreshTrash = new Button { Text = "更新", Left = 320, Top = 5, Width = 80, Height = 26 };
            _btnClearTrash = new Button
            {
                Text = "一括クリア",
                Left = 410,
                Top = 5,
                Width = 100,
                Height = 26,
                BackColor = Color.FromArgb(196, 43, 28),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnRefreshTrash.Click += (s, e) => RefreshTrash();
            _btnClearTrash.Click += BtnClearTrash_Click;
            pnlTop.Controls.AddRange(new Control[] { _lblTrashSize, _btnRefreshTrash, _btnClearTrash });
            tab.Controls.Add(pnlTop);

            var pnlFolder = new Panel { Dock = DockStyle.Top, Height = 36 };
            var lblFolder = new Label { Text = "格納先フォルダ:", Left = 8, Top = 10, Width = 100, ForeColor = Color.DimGray };
            _txtTrashFolder = new TextBox { Left = 110, Top = 6, Width = 320 };
            _btnTrashFolderBrowse = new Button { Text = "参照...", Left = 438, Top = 5, Width = 60, Height = 26 };
            _btnOpenTrashFolder = new Button { Text = "フォルダを開く", Left = 502, Top = 5, Width = 110, Height = 26 };
            _btnTrashFolderBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Trash（ゴミ箱）の格納先フォルダを選択", SelectedPath = _txtTrashFolder.Text })
                {
                    if (dlg.ShowDialog() == DialogResult.OK) _txtTrashFolder.Text = dlg.SelectedPath;
                }
            };
            _btnOpenTrashFolder.Click += (s, e) =>
            {
                try
                {
                    var path = _settings.GetEffectiveTrashRoot();
                    if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                    System.Diagnostics.Process.Start("explorer.exe", path);
                }
                catch { }
            };
            pnlFolder.Controls.AddRange(new Control[] { lblFolder, _txtTrashFolder, _btnTrashFolderBrowse, _btnOpenTrashFolder });
            tab.Controls.Add(pnlFolder);

            var lbl = new Label
            {
                Text = "Trashフォルダ内ファイル（旧バージョンの各ドライブ直下フォルダも含む）:",
                Dock = DockStyle.None,
                Left = 8,
                Top = 78,
                Width = 600,
                ForeColor = Color.Gray
            };
            tab.Controls.Add(lbl);

            _lbTrashFiles = new ListBox
            {
                Left = 8,
                Top = 100,
                Width = tab.Width - 20,
                Height = 300,
                Font = new Font("Consolas", 9f),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(_lbTrashFiles);
        }

        // ════════════════════════════════════════════════════════
        // データ操作
        // ════════════════════════════════════════════════════════
        private void LoadValues()
        {
            _numPort.Value = _settings.Port;
            _txtServerUrl.Text = _settings.MachCoreServerUrl;
            _chkAutoStart.Checked = _settings.AutoStart;
            _chkBalloon.Checked = _settings.ShowBalloonNotify;
            _chkVerbose.Checked = _settings.VerboseLog;
            _txtIconPath.Text = _settings.CustomIconPath;
            _txtTrashFolder.Text = _settings.GetEffectiveTrashRoot();
            _txtUsbDrivePath.Text = _settings.UsbDrivePath;
        }

        private void RefreshStats()
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            this.Invoke((MethodInvoker)(() =>
            {
                var (moved, error) = _stats.GetToday();
                _lblStatus.Text     = "稼働中 ✅";
                _lblStatus.ForeColor = Color.Green;
                _lblMoved.Text      = $"{moved} 件";
                _lblError.Text      = $"{error} 件";
                _lblError.ForeColor = error > 0 ? Color.Red : Color.Green;
            }));
        }

        private void RefreshLog()
        {
            var lines   = _logger.GetRecentLines(300);
            var sb      = new StringBuilder();
            foreach (var l in lines) sb.AppendLine(l);
            _rtbLog.Text = sb.ToString();
            var sizeKb   = _logger.GetLogSizeBytes() / 1024;
            _lblLogSize.Text = $"ログ合計: {sizeKb:N0} KB（最新300行表示）";

            // キーワードカラー
            _rtbLog.SelectAll();
            _rtbLog.SelectionColor = Color.LightGreen;
            HighlightKeyword("[ERROR]", Color.OrangeRed);
            HighlightKeyword("[WARN ]", Color.Yellow);
            HighlightKeyword("[DEBUG]", Color.DarkGray);
        }

        private void RefreshTrash()
        {
            long total = _fileOps.GetTrashSizeBytes();
            _lblTrashSize.Text = $"Trash合計サイズ: {FormatBytes(total)}";
            _lbTrashFiles.Items.Clear();

            try
            {
                var trashRoot = _settings.GetEffectiveTrashRoot();
                if (Directory.Exists(trashRoot))
                {
                    foreach (var f in Directory.GetFiles(trashRoot, "*", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(f);
                        _lbTrashFiles.Items.Add($"[Trash] {fi.Name}  ({FormatBytes(fi.Length)})  {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                    }
                }

                foreach (var d in System.IO.DriveInfo.GetDrives())
                {
                    if (!d.IsReady) continue;
                    var legacyDir = System.IO.Path.Combine(d.RootDirectory.FullName, ".machcore_trash");
                    if (!Directory.Exists(legacyDir)) continue;
                    foreach (var f in Directory.GetFiles(legacyDir, "*", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(f);
                        _lbTrashFiles.Items.Add($"[旧:{d.Name}] {fi.Name}  ({FormatBytes(fi.Length)})  {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                    }
                }

                if (_lbTrashFiles.Items.Count == 0)
                    _lbTrashFiles.Items.Add("（Trashフォルダは空です）");
            }
            catch { }
        }

        private void BtnPing_Click(object sender, EventArgs e)
        {
            _btnPing.Enabled  = false;
            _lblPing.Text     = "確認中...";
            _lblPing.ForeColor = Color.Gray;

            var url = _settings.MachCoreServerUrl;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string result; Color color;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 5000;
                    req.ServerCertificateValidationCallback = (a, b, c, d) => true;
                    using (var res = (HttpWebResponse)req.GetResponse())
                    {
                        result = $"✅ {(int)res.StatusCode} {res.StatusDescription}";
                        color  = Color.Green;
                    }
                }
                catch (Exception ex)
                {
                    result = $"❌ {ex.Message}";
                    color  = Color.OrangeRed;
                }

                if (!this.IsDisposed && this.IsHandleCreated)
                    this.Invoke((MethodInvoker)(() =>
                    {
                        _lblPing.Text      = result;
                        _lblPing.ForeColor = color;
                        _btnPing.Enabled   = true;
                    }));
            });
        }

        private void BtnClearTrash_Click(object sender, EventArgs e)
        {
            var count    = _lbTrashFiles.Items.Count;
            var confirm  = MessageBox.Show(
                $"Trashフォルダ内の全ファイルを完全に削除します。\n{count}件のファイルが対象です。\n\nこの操作は元に戻せません。実行しますか？",
                "Trash一括クリア確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            var (deleted, freed) = _fileOps.ClearTrash();
            MessageBox.Show($"✅ {deleted}件のファイルを削除しました。\n解放容量: {FormatBytes(freed)}",
                "クリア完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshTrash();
        }

        private void BtnIconBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title  = "アイコンファイルを選択",
                Filter = "アイコンファイル (*.ico)|*.ico|すべてのファイル (*.*)|*.*",
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    _txtIconPath.Text = dlg.FileName;
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            int newPort = (int)_numPort.Value;
            if (newPort != _currentPort)
            {
                var r = MessageBox.Show(
                    $"ポートを {_currentPort} → {newPort} に変更します。\nAgentが再起動されます。続行しますか？",
                    "ポート変更確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }

            _settings.Port = newPort;
            _settings.MachCoreServerUrl = _txtServerUrl.Text.Trim();
            _settings.AutoStart = _chkAutoStart.Checked;
            _settings.ShowBalloonNotify = _chkBalloon.Checked;
            _settings.VerboseLog = _chkVerbose.Checked;
            _settings.CustomIconPath = _txtIconPath.Text.Trim();
            _settings.TrashFolderPath = _txtTrashFolder.Text.Trim();
            _settings.UsbDrivePath = _txtUsbDrivePath.Text.Trim();
            _settings.Save();

            _onSaved?.Invoke(_settings);
            this.Close();
        }

        // ════════════════════════════════════════════════════════
        // ユーティリティ
        // ════════════════════════════════════════════════════════
        private void HighlightKeyword(string keyword, Color color)
        {
            int start = 0;
            while (true)
            {
                int idx = _rtbLog.Text.IndexOf(keyword, start, StringComparison.Ordinal);
                if (idx < 0) break;
                _rtbLog.Select(idx, keyword.Length);
                _rtbLog.SelectionColor = color;
                start = idx + keyword.Length;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)         return $"{bytes} B";
            if (bytes < 1024 * 1024)  return $"{bytes / 1024.0:F1} KB";
            return                           $"{bytes / (1024.0 * 1024):F1} MB";
        }

        private static Label AddLabel(Control parent, string text, int x, int y,
                                      Color? color = null, bool bold = false)
        {
            var lbl = new Label
            {
                Text      = text,
                Left      = x,
                Top       = y,
                Width     = 180,
                ForeColor = color ?? SystemColors.ControlText,
                Font      = bold ? new Font(SystemFonts.DefaultFont, FontStyle.Bold) : SystemFonts.DefaultFont,
                AutoSize  = true,
            };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private static Label AddValueLabel(Control parent, string text, int x, int y,
                                           Color? color = null)
        {
            var lbl = new Label
            {
                Text      = text,
                Left      = x,
                Top       = y,
                Width     = 350,
                ForeColor = color ?? SystemColors.ControlText,
                AutoSize  = true,
            };
            parent.Controls.Add(lbl);
            return lbl;
        }
    }
}
