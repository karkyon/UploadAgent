using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace UploadAgent.Forms
{
    /// <summary>
    /// フォルダ内の画像ファイルをサムネイル付きグリッドで一覧表示し、
    /// チェックボックスで選択させるフォーム。
    /// 廃止前のWeb側複数選択プレビューモーダルと同等の体験をAgent側で提供する。
    /// </summary>
    public class FileGridPickerForm : Form
    {
        private readonly List<string> _allFiles;
        private readonly Dictionary<string, CheckBox> _checkBoxes = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, Image> _thumbCache = new Dictionary<string, Image>();
        private FlowLayoutPanel _flow;
        private Button _btnOk;
        private Button _btnCancel;
        private Button _btnSelectAll;
        private Button _btnSelectNone;
        private Label _lblCount;

        public List<string> SelectedFiles { get; private set; } = new List<string>();
        public bool WasCancelled { get; private set; } = true;

        private static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

        public FileGridPickerForm(string folderPath, IEnumerable<string> files)
        {
            _allFiles = files.ToList();
            InitializeComponent(folderPath);
            LoadThumbnails();
        }

        private void InitializeComponent(string folderPath)
        {
            this.Text = $"MachCore - 取り込むファイルを選択 ({Path.GetFileName(folderPath)})";
            this.Size = new Size(820, 640);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true; // 最前面表示
            this.MinimumSize = new Size(500, 400);

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 8, 10, 0) };
            _lblCount = new Label { Text = $"{_allFiles.Count} 件のファイル", AutoSize = true, Top = 12, Left = 10, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
            _btnSelectAll = new Button { Text = "すべて選択", Width = 90, Height = 26, Top = 8, Left = 160 };
            _btnSelectNone = new Button { Text = "すべて解除", Width = 90, Height = 26, Top = 8, Left = 258 };
            _btnSelectAll.Click += (s, e) => SetAllChecked(true);
            _btnSelectNone.Click += (s, e) => SetAllChecked(false);
            topPanel.Controls.AddRange(new Control[] { _lblCount, _btnSelectAll, _btnSelectNone });

            _flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(10),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
            };

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(10) };
            _btnOk = new Button { Text = "選択したファイルを取り込む", Width = 220, Height = 36, Left = 0, Top = 10, BackColor = Color.FromArgb(13, 148, 136), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _btnCancel = new Button { Text = "キャンセル", Width = 100, Height = 36, Top = 10 };
            _btnOk.Click += BtnOk_Click;
            _btnCancel.Click += (s, e) => { WasCancelled = true; this.Close(); };
            bottomPanel.Controls.AddRange(new Control[] { _btnOk, _btnCancel });

            this.Controls.Add(_flow);
            this.Controls.Add(bottomPanel);
            this.Controls.Add(topPanel);

            this.Resize += (s, e) => PositionBottomButtons(bottomPanel);
            PositionBottomButtons(bottomPanel);

            BuildGrid();
        }

        private void PositionBottomButtons(Panel bottomPanel)
        {
            _btnOk.Left = bottomPanel.Width - _btnOk.Width - _btnCancel.Width - 20;
            _btnCancel.Left = bottomPanel.Width - _btnCancel.Width - 10;
        }

        private void BuildGrid()
        {
            foreach (var path in _allFiles)
            {
                var card = new Panel { Width = 160, Height = 190, Margin = new Padding(6), BorderStyle = BorderStyle.FixedSingle };

                var pic = new PictureBox
                {
                    Width = 148,
                    Height = 110,
                    Top = 6,
                    Left = 6,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(245, 245, 245),
                    Tag = path,
                };

                var nameLabel = new Label
                {
                    Text = Path.GetFileName(path),
                    Top = 120,
                    Left = 6,
                    Width = 148,
                    Height = 32,
                    AutoEllipsis = true,
                    Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f),
                };

                var chk = new CheckBox { Text = "選択", Top = 156, Left = 6, Checked = true };
                _checkBoxes[path] = chk;

                // カード全体クリックでもチェックトグル（画像クリックしやすく）
                pic.Click += (s, e) => chk.Checked = !chk.Checked;

                card.Controls.Add(pic);
                card.Controls.Add(nameLabel);
                card.Controls.Add(chk);
                _flow.Controls.Add(card);
            }
        }

        private void LoadThumbnails()
        {
            // UIスレッドをブロックしないよう、サムネイル生成は非同期で行い完了次第差し替える
            foreach (var path in _allFiles)
            {
                var ext = Path.GetExtension(path);
                if (!ImageExts.Contains(ext)) continue;

                var capturedPath = path;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        using (var fs = new FileStream(capturedPath, FileMode.Open, FileAccess.Read))
                        using (var original = Image.FromStream(fs, false, false))
                        {
                            var thumb = new Bitmap(original, new Size(148, 110));
                            if (this.IsHandleCreated && !this.IsDisposed)
                            {
                                this.Invoke(new Action(() =>
                                {
                                    var pic = FindPictureBox(capturedPath);
                                    if (pic != null && !this.IsDisposed) pic.Image = thumb;
                                }));
                            }
                        }
                    }
                    catch { /* サムネイル生成失敗は無視（プレースホルダのまま） */ }
                });
            }
        }

        private PictureBox FindPictureBox(string path)
        {
            foreach (Control card in _flow.Controls)
            {
                foreach (Control c in card.Controls)
                {
                    if (c is PictureBox pic && (string)pic.Tag == path) return pic;
                }
            }
            return null;
        }

        private void SetAllChecked(bool value)
        {
            foreach (var chk in _checkBoxes.Values) chk.Checked = value;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            SelectedFiles = _checkBoxes.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToList();
            if (SelectedFiles.Count == 0)
            {
                MessageBox.Show("1件以上選択してください。", "MachCore", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            WasCancelled = false;
            this.Close();
        }
    }
}