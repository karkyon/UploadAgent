using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace UploadAgent.Forms
{
    /// <summary>
    /// フォルダ内の画像ファイルをサムネイル付きグリッドで一覧表示し、
    /// チェックボックスで選択させるフォーム。
    /// MachCore Web版の複数選択プレビューモーダルと同等のデザイン・体験を提供する。
    /// </summary>
    public class FileGridPickerForm : Form
    {
        // ── Web版配色（teal系） ──
        private static readonly Color ColorTeal600 = Color.FromArgb(13, 148, 136);
        private static readonly Color ColorTeal700 = Color.FromArgb(15, 118, 110);
        private static readonly Color ColorTeal100 = Color.FromArgb(204, 251, 241);
        private static readonly Color ColorTeal50 = Color.FromArgb(240, 253, 250);
        private static readonly Color ColorSlate50 = Color.FromArgb(248, 250, 252);
        private static readonly Color ColorSlate100 = Color.FromArgb(241, 245, 249);
        private static readonly Color ColorSlate200 = Color.FromArgb(226, 232, 240);
        private static readonly Color ColorSlate300 = Color.FromArgb(203, 213, 225);
        private static readonly Color ColorSlate400 = Color.FromArgb(148, 163, 184);
        private static readonly Color ColorSlate600 = Color.FromArgb(71, 85, 105);
        private static readonly Color ColorSlate700 = Color.FromArgb(51, 65, 85);
        private static readonly Color ColorSlate800 = Color.FromArgb(30, 41, 59);
        private static readonly Color ColorRed50 = Color.FromArgb(254, 242, 242);
        private static readonly Color ColorRed200 = Color.FromArgb(254, 202, 202);
        private static readonly Color ColorRed600 = Color.FromArgb(220, 38, 38);
        private static readonly Color ColorYellowBorder = Color.FromArgb(250, 204, 21);
        private static readonly Color ColorYellowBg = Color.FromArgb(254, 252, 232);

        private const int ThumbW = 220, ThumbH = 160;
        private const int CardW = 236, CardH = 236;
        private const int Cols = 6;

        private readonly List<string> _allFiles;
        private readonly Dictionary<string, WebCheckBox> _checkBoxes = new Dictionary<string, WebCheckBox>();
        private readonly Dictionary<string, Panel> _cards = new Dictionary<string, Panel>();
        private FlowLayoutPanel _flow;
        private RoundButton _btnOk;
        private RoundButton _btnCancel;
        private RoundButton _btnSelectAll;
        private RoundButton _btnSelectNone;
        private Label _lblCount;
        private Label _lblTitle;

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
            int width = Cols * (CardW + 14) + 60;
            this.Text = "MachCore - 取り込むファイルを選択";
            this.Size = new Size(Math.Max(width, 900), 760);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true;
            this.MinimumSize = new Size(700, 500);
            this.BackColor = ColorSlate100;
            this.Font = new Font("Yu Gothic UI", 9f);

            // ── ヘッダー ──
            var headerPanel = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.White, Padding = new Padding(24, 0, 24, 0) };
            headerPanel.Paint += (s, e) => {
                using (var pen = new Pen(ColorSlate200, 1))
                    e.Graphics.DrawLine(pen, 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
            };

            _lblTitle = new Label
            {
                Text = $"📁 {Path.GetFileName(folderPath)}",
                AutoSize = true,
                Top = 16,
                Left = 24,
                Font = new Font("Yu Gothic UI", 12.5f, FontStyle.Bold),
                ForeColor = ColorSlate800,
            };
            _lblCount = new Label
            {
                Text = $"{_allFiles.Count} 件のファイル",
                AutoSize = true,
                Top = 44,
                Left = 24,
                Font = new Font("Yu Gothic UI", 9f),
                ForeColor = ColorSlate400,
            };
            _btnSelectAll = new RoundButton("すべて選択", 104, 34, Color.White, ColorSlate700, ColorSlate300, ColorTeal50);
            _btnSelectAll.Top = 19; _btnSelectAll.Left = 300;
            _btnSelectNone = new RoundButton("すべて解除", 104, 34, Color.White, ColorSlate700, ColorSlate300, ColorSlate50);
            _btnSelectNone.Top = 19; _btnSelectNone.Left = 412;
            _btnSelectAll.Click += (s, e) => SetAllChecked(true);
            _btnSelectNone.Click += (s, e) => SetAllChecked(false);

            headerPanel.Controls.AddRange(new Control[] { _lblTitle, _lblCount, _btnSelectAll, _btnSelectNone });

            // ── グリッド本体 ──
            _flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(24),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = ColorSlate100,
            };

            // ── フッター ──
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Color.White, Padding = new Padding(24, 0, 24, 0) };
            bottomPanel.Paint += (s, e) => {
                using (var pen = new Pen(ColorSlate200, 1))
                    e.Graphics.DrawLine(pen, 0, 0, bottomPanel.Width, 0);
            };

            _btnOk = new RoundButton("📥  選択したファイルを取り込む", 280, 42, ColorTeal600, Color.White, ColorTeal600, ColorTeal700);
            _btnOk.Font = new Font("Yu Gothic UI", 9.5f, FontStyle.Bold);
            _btnOk.Top = 17;
            _btnCancel = new RoundButton("キャンセル", 120, 42, ColorRed50, ColorRed600, ColorRed200, ColorRed200);
            _btnCancel.Font = new Font("Yu Gothic UI", 9.5f, FontStyle.Bold);
            _btnCancel.Top = 17;
            _btnOk.Click += BtnOk_Click;
            _btnCancel.Click += (s, e) => { WasCancelled = true; this.Close(); };

            bottomPanel.Controls.AddRange(new Control[] { _btnOk, _btnCancel });

            this.Controls.Add(_flow);
            this.Controls.Add(bottomPanel);
            this.Controls.Add(headerPanel);

            this.Resize += (s, e) => PositionBottomButtons(bottomPanel);
            this.Shown += (s, e) => PositionBottomButtons(bottomPanel);

            BuildGrid();
        }

        private void PositionBottomButtons(Panel bottomPanel)
        {
            _btnOk.Left = bottomPanel.Width - _btnOk.Width - _btnCancel.Width - 36;
            _btnCancel.Left = bottomPanel.Width - _btnCancel.Width - 24;
        }

        private void BuildGrid()
        {
            foreach (var path in _allFiles)
            {
                var card = new Panel
                {
                    Width = CardW,
                    Height = CardH,
                    Margin = new Padding(7),
                    BackColor = Color.White,
                    Tag = path,
                };
                card.Paint += (s, e) => PaintCardBorder(card, e.Graphics);
                _cards[path] = card;

                var picBorder = new Panel
                {
                    Width = ThumbW + 4,
                    Height = ThumbH + 4,
                    Top = 8,
                    Left = 4,
                    BackColor = ColorSlate50,
                };

                var pic = new PictureBox
                {
                    Width = ThumbW,
                    Height = ThumbH,
                    Top = 2,
                    Left = 2,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = ColorSlate50,
                    Tag = path,
                    Cursor = Cursors.Hand,
                };
                picBorder.Controls.Add(pic);

                var nameLabel = new Label
                {
                    Text = Path.GetFileName(path),
                    Top = ThumbH + 16,
                    Left = 8,
                    Width = CardW - 16,
                    Height = 18,
                    AutoEllipsis = true,
                    Font = new Font("Yu Gothic UI", 8.5f, FontStyle.Bold),
                    ForeColor = ColorSlate700,
                };

                var chk = new WebCheckBox { Top = ThumbH + 36, Left = 8, Checked = true };
                chk.CheckedChanged += (s, e) => { card.Invalidate(); };
                _checkBoxes[path] = chk;

                pic.Click += (s, e) => chk.Checked = !chk.Checked;

                card.Controls.Add(picBorder);
                card.Controls.Add(nameLabel);
                card.Controls.Add(chk);
                _flow.Controls.Add(card);
            }
        }

        private void PaintCardBorder(Panel card, Graphics g)
        {
            bool selected = _checkBoxes.TryGetValue((string)card.Tag, out var chk) && chk.Checked;
            var borderColor = selected ? ColorYellowBorder : ColorSlate200;
            var bg = selected ? ColorYellowBg : Color.White;
            card.BackColor = bg;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedPath(1, 1, card.Width - 3, card.Height - 3, 10))
            using (var pen = new Pen(borderColor, 2))
            {
                g.DrawPath(pen, path);
            }
        }

        private static GraphicsPath RoundedPath(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void LoadThumbnails()
        {
            foreach (var path in _allFiles)
            {
                var ext = Path.GetExtension(path);
                if (!ImageExts.Contains(ext)) continue;

                var capturedPath = path;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(capturedPath);
                        using (var ms = new MemoryStream(bytes))
                        using (var original = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false))
                        {
                            var thumb = new Bitmap(ThumbW, ThumbH);
                            using (var g = Graphics.FromImage(thumb))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.CompositingQuality = CompositingQuality.HighQuality;
                                g.SmoothingMode = SmoothingMode.AntiAlias;

                                float scale = Math.Min((float)ThumbW / original.Width, (float)ThumbH / original.Height);
                                int dw = (int)(original.Width * scale);
                                int dh = (int)(original.Height * scale);
                                int dx = (ThumbW - dw) / 2;
                                int dy = (ThumbH - dh) / 2;

                                g.Clear(ColorSlate50);
                                g.DrawImage(original, dx, dy, dw, dh);
                            }

                            if (this.IsHandleCreated && !this.IsDisposed)
                            {
                                this.Invoke(new Action(() =>
                                {
                                    var pic = FindPictureBox(capturedPath);
                                    if (pic != null && !this.IsDisposed)
                                    {
                                        var old = pic.Image;
                                        pic.Image = thumb;
                                        old?.Dispose();
                                    }
                                }));
                            }
                            else
                            {
                                thumb.Dispose();
                            }
                        }
                    }
                    catch { }
                });
            }
        }

        private PictureBox FindPictureBox(string path)
        {
            if (!_cards.TryGetValue(path, out var card)) return null;
            foreach (Control c1 in card.Controls)
            {
                if (c1 is Panel border)
                {
                    foreach (Control c2 in border.Controls)
                    {
                        if (c2 is PictureBox pic && (string)pic.Tag == path) return pic;
                    }
                }
            }
            return null;
        }

        private void SetAllChecked(bool value)
        {
            foreach (var kv in _checkBoxes) kv.Value.Checked = value;
            foreach (var card in _cards.Values) card.Invalidate();
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

    // ════════════════════════════════════════════════════════
    // Web版風カスタムボタン（角丸・ホバー色変化）
    // ════════════════════════════════════════════════════════
    internal class RoundButton : Button
    {
        private readonly Color _normalBg;
        private readonly Color _hoverBorder;
        private Color _borderColor;
        private bool _isHover;

        public RoundButton(string text, int w, int h, Color bg, Color fg, Color border, Color hoverBorder)
        {
            this.Text = text;
            this.Width = w;
            this.Height = h;
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.BackColor = bg;
            this.ForeColor = fg;
            this.Font = new Font("Yu Gothic UI", 9f, FontStyle.Bold);
            this.Cursor = Cursors.Hand;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            _normalBg = bg;
            _borderColor = border;
            this.MouseEnter += (s, e) => { _isHover = true; this.Invalidate(); };
            this.MouseLeave += (s, e) => { _isHover = false; this.Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            int radius = 8;

            using (var path = RoundedPath(rect, radius))
            {
                Color bg = _isHover ? ControlPaint.Light(_normalBg, 0.08f) : _normalBg;
                if (_normalBg == Color.White) bg = _isHover ? Color.FromArgb(248, 250, 252) : Color.White;
                using (var brush = new SolidBrush(bg))
                    g.FillPath(brush, path);
                using (var pen = new Pen(_borderColor, 1.4f))
                    g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, this.Text, this.Font, rect, this.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // ════════════════════════════════════════════════════════
    // Web版風カスタムチェックボックス（角丸・teal色のチェック）
    // ════════════════════════════════════════════════════════
    internal class WebCheckBox : CheckBox
    {
        private static readonly Color ColorTeal600 = Color.FromArgb(13, 148, 136);
        private static readonly Color ColorSlate300 = Color.FromArgb(203, 213, 225);
        private static readonly Color ColorSlate700 = Color.FromArgb(51, 65, 85);

        public WebCheckBox()
        {
            this.Text = "選択";
            this.AutoSize = true;
            this.Font = new Font("Yu Gothic UI", 8.5f);
            this.ForeColor = ColorSlate700;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int boxSize = 16;
            int boxTop = (this.Height - boxSize) / 2;
            var boxRect = new Rectangle(0, boxTop, boxSize, boxSize);

            using (var path = RoundedRect(boxRect, 4))
            {
                if (this.Checked)
                {
                    using (var brush = new SolidBrush(ColorTeal600))
                        g.FillPath(brush, path);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.White))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(ColorSlate300, 1.5f))
                        g.DrawPath(pen, path);
                }
            }

            if (this.Checked)
            {
                using (var pen = new Pen(Color.White, 2f))
                {
                    g.DrawLine(pen, boxRect.Left + 3, boxTop + 8, boxRect.Left + 6, boxTop + 11);
                    g.DrawLine(pen, boxRect.Left + 6, boxTop + 11, boxRect.Left + 13, boxTop + 4);
                }
            }

            var textRect = new Rectangle(boxSize + 6, 0, this.Width - boxSize - 6, this.Height);
            TextRenderer.DrawText(g, this.Text, this.Font, textRect, this.ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}