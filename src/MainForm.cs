using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AutovisorPanel
{
    internal class MainForm : Form
    {
        private readonly string root;
        private readonly string configPath;
        private readonly string logsDir;
        private readonly CredentialStore cred;
        private readonly StudyRunner runner;

        private Pill pill;
        private Label attention;
        private InputBox boxUser;
        private InputBox boxPass;
        private Pill credPill;
        private ComboBox cbDriver;
        private InputBox boxExe;
        private NumericUpDown numMaxTime;
        private NumericUpDown numSpeed;
        private CheckBox chkSound;
        private CheckBox chkCaptcha;
        private CheckBox chkHide;
        private CheckBox chkDonate;
        private TextBox boxUrls;
        private FlatButton btnStart;
        private FlatButton btnStop;
        private FlatButton btnSave;

        private Label lblUpdated;
        private Label lblIdle;
        private Label lblLesson;
        private Label lblProgress;
        private Bar bar;
        private Label lblDone;
        private TextBox boxLog;
        private CheckBox chkGameGuard;
        private Label hintTip;

        private Timer tickStatus;
        private Timer tickGuard;
        private bool lastAttention;
        private bool guardPaused;

        private readonly GpuProbe probe = new GpuProbe();
        private Timer tickGame;
        private bool gameActive;
        private bool pausedByGame;
        private int hotCount;
        private int coldCount;
        private string gameDetail = "";

        private NotifyIcon tray;
        private bool stuckNotified;
        private DateTime lastStuckNotify = DateTime.MinValue;

        private const double StuckMinutes = 10;      // 日志静默超过该分钟数视为疑似卡死
        private const int StuckRenotifyMin = 15;     // 持续卡死时的重复提醒间隔

        private Panel content;
        private Card configCard;
        private Card statusCard;
        private GripBar grip;
        private Panel guide;
        private bool draggingGrip;
        private int dragStartX;
        private int dragStartWidth;
        private int lastDragTick;
        private int pendingLeft = 640;
        private int leftWidth = 640;

        private const int CardMargin = 24;
        private const int TopPad = 12;
        private const int BottomPad = 24;
        private const int GripW = 12;
        private const int GripGap = 6;
        private const int MinConfig = 640;
        private const int MinStatus = 360;

        // WS_EX_COMPOSITED：整窗离屏合成，消除子控件逐个绘制造成的断层闪烁
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        public MainForm(string root)
        {
            this.root = root;
            configPath = Path.Combine(root, "config.ini");
            logsDir = Path.Combine(root, "logs");
            cred = new CredentialStore(Path.Combine(root, "data", "cred.bin"));
            runner = new StudyRunner(root);

            Text = "Autovisor 面板";
            ClientSize = new Size(1150, 720);
            MinimumSize = new Size(1130, 680);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            BackColor = T.Bg;
            Font = T.F(9f);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;

            BuildHeader();
            BuildContent();
            BuildConfigCard();
            BuildStatusCard();

            tickStatus = new Timer();
            tickStatus.Interval = 3000;
            tickStatus.Tick += delegate { RefreshStatus(); };

            tickGuard = new Timer();
            tickGuard.Interval = 15000;
            tickGuard.Tick += delegate { GuardTick(); };

            tickGame = new Timer();
            tickGame.Interval = 5000;
            tickGame.Tick += delegate { GameTick(); };

            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Warning;
            tray.Text = "Autovisor 面板";
            tray.Visible = false;
            tray.BalloonTipClicked += delegate { RestorePanel(); };


            Load += delegate
            {
                LoadConfig();
                LayoutContent();
                if (!runner.IsRunning) StudyRunner.CleanTempConfigs();
                RefreshStatus();
                UpdateGameHint();
                tickStatus.Start();
                tickGuard.Start();
                tickGame.Start();
            };

            FormClosing += OnClosing;
        }

        // ---------------- 顶栏 ----------------
        private void BuildHeader()
        {
            Panel header = new Panel();
            header.Location = new Point(0, 0);
            header.Size = new Size(1150, 84);
            header.BackColor = T.Card;
            header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(header);

            Label title = new Label();
            title.Text = "Autovisor 面板";
            title.Font = T.F(15f, FontStyle.Bold);
            title.ForeColor = T.Text;
            title.AutoSize = true;
            title.BackColor = Color.Transparent;
            title.Location = new Point(24, 10);
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "智慧树课程自动学习 · 本地配置与运行控制";
            sub.Font = T.F(9f);
            sub.ForeColor = T.Sub;
            sub.AutoSize = true;
            sub.BackColor = Color.Transparent;
            sub.Location = new Point(26, 50);
            header.Controls.Add(sub);

            attention = new Label();
            attention.Text = "";
            attention.Font = T.F(9f);
            attention.ForeColor = T.Err;
            attention.AutoSize = false;
            attention.TextAlign = ContentAlignment.MiddleRight;
            attention.BackColor = Color.Transparent;
            attention.Location = new Point(560, 30);
            attention.Size = new Size(330, 22);
            attention.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            attention.Visible = false;
            header.Controls.Add(attention);

            pill = new Pill();
            pill.Location = new Point(1030, 28);
            pill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pill.Set("未运行", T.Soft, T.Sub);
            header.Controls.Add(pill);

            Panel line = new Panel();
            line.Location = new Point(0, 83);
            line.Size = new Size(1150, 1);
            line.BackColor = T.Line;
            line.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(line);
        }

        // ---------------- 内容区与可拖拽分隔把手 ----------------
        private void BuildContent()
        {
            content = new BufferedPanel();
            content.Location = new Point(0, 84);
            content.Size = new Size(ClientSize.Width, ClientSize.Height - 84);
            content.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            content.BackColor = T.Bg;
            Controls.Add(content);

            configCard = new Card();
            configCard.Caption = "配置";
            configCard.SubCaption = "账号、浏览器与课程设置";
            configCard.Size = new Size(640, 600);
            content.Controls.Add(configCard);

            statusCard = new Card();
            statusCard.Caption = "运行状态";
            statusCard.SubCaption = "每 3 秒刷新，数据来自 logs 目录";
            statusCard.Size = new Size(438, 600);
            content.Controls.Add(statusCard);

            grip = new GripBar();
            grip.MouseDown += GripDown;
            grip.MouseMove += GripMove;
            grip.MouseUp += GripUp;
            content.Controls.Add(grip);

            guide = new Panel();
            guide.Size = new Size(2, 1);
            guide.BackColor = T.Accent;
            guide.Visible = false;
            content.Controls.Add(guide);
            guide.BringToFront();

            content.Resize += delegate { LayoutContent(); };
        }

        private void LayoutContent()
        {
            if (content == null) return;

            int w = content.ClientSize.Width;
            int h = content.ClientSize.Height;
            int cardH = h - TopPad - BottomPad;
            if (cardH < 320) cardH = 320;

            int maxLeft = w - CardMargin - GripW - GripGap * 2 - MinStatus - CardMargin;
            if (leftWidth > maxLeft) leftWidth = maxLeft;
            if (leftWidth < MinConfig) leftWidth = MinConfig;

            int gx = CardMargin + leftWidth + GripGap;
            int sx = gx + GripW + GripGap;
            int oldGx = grip.Left;

            content.SuspendLayout();
            try
            {
                configCard.SetBounds(CardMargin, TopPad, leftWidth, cardH);
                grip.SetBounds(gx, 0, GripW, h);
                statusCard.SetBounds(sx, TopPad, w - sx - CardMargin, cardH);
            }
            finally
            {
                content.ResumeLayout(false);
            }

            // 只擦除把手移动经过的窄条，卡片自身由尺寸变化触发重绘
            int from = Math.Min(oldGx, gx);
            int to = Math.Max(oldGx, gx) + GripW;
            content.Invalidate(new Rectangle(from - 2, 0, to - from + 4, h), false);
        }

        private void GripDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            draggingGrip = true;
            dragStartX = Cursor.Position.X;
            dragStartWidth = leftWidth;
            pendingLeft = leftWidth;
            lastDragTick = 0;
            tickStatus.Stop();

            guide.Height = content.ClientSize.Height;
            guide.Visible = true;
            ShowGuide(Cursor.Position.X);
        }

        private void GripMove(object sender, MouseEventArgs e)
        {
            if (!draggingGrip) return;

            int now = Environment.TickCount;
            if (now - lastDragTick < 16) return;
            lastDragTick = now;

            ShowGuide(Cursor.Position.X);
        }

        private void GripUp(object sender, MouseEventArgs e)
        {
            if (!draggingGrip) return;

            ShowGuide(Cursor.Position.X);
            draggingGrip = false;
            guide.Visible = false;

            if (pendingLeft != leftWidth)
            {
                leftWidth = pendingLeft;
                LayoutContent();
            }

            tickStatus.Start();
            RefreshStatus();
        }

        // 拖动时只更新一条竖向预览线，不做任何重排
        private void ShowGuide(int screenX)
        {
            int target = dragStartWidth + (screenX - dragStartX);
            if (content == null) return;

            int maxLeft = content.ClientSize.Width - CardMargin - GripW - GripGap * 2 - MinStatus - CardMargin;
            if (target > maxLeft) target = maxLeft;
            if (target < MinConfig) target = MinConfig;
            pendingLeft = target;

            int oldX = guide.Left;
            guide.Left = CardMargin + target + GripGap + GripW / 2 - 1;

            int h = content.ClientSize.Height;
            content.Invalidate(new Rectangle(oldX - 3, 0, 8, h), false);
            content.Invalidate(new Rectangle(guide.Left - 3, 0, 8, h), false);
        }

        // ---------------- 配置卡片 ----------------
        private void BuildConfigCard()
        {
            Card card = configCard;

            AddLabel(card, "账号", 18, 72, 52);
            boxUser = new InputBox();
            boxUser.Location = new Point(72, 66);
            boxUser.Size = new Size(232, 30);
            card.Controls.Add(boxUser);

            AddLabel(card, "密码", 322, 72, 44);
            boxPass = new InputBox();
            boxPass.Location = new Point(368, 66);
            boxPass.Size = new Size(170, 30);
            boxPass.EnableReveal();
            card.Controls.Add(boxPass);

            credPill = new Pill();
            credPill.Location = new Point(546, 68);
            credPill.Size = new Size(76, 26);
            credPill.Set("未保存", T.Soft, T.Sub);
            card.Controls.Add(credPill);

            AddLabel(card, "浏览器", 18, 116, 52);
            cbDriver = new ComboBox();
            cbDriver.Location = new Point(84, 110);
            cbDriver.Size = new Size(118, 30);
            cbDriver.DropDownStyle = ComboBoxStyle.DropDownList;
            cbDriver.FlatStyle = FlatStyle.Flat;
            cbDriver.Font = T.F(9.5f);
            cbDriver.Items.Add("Edge");
            cbDriver.Items.Add("Chrome");
            card.Controls.Add(cbDriver);

            AddLabel(card, "路径", 218, 116, 40);
            boxExe = new InputBox();
            boxExe.Location = new Point(262, 110);
            boxExe.Size = new Size(360, 30);
            boxExe.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(boxExe);

            AddLabel(card, "每门课上限", 18, 160, 76);
            numMaxTime = MakeNumeric(0, 600, 0);
            numMaxTime.Location = new Point(98, 156);
            numMaxTime.Size = new Size(78, 24);
            card.Controls.Add(numMaxTime);
            AddHint(card, "分钟（0 = 不限）", 184, 160, 110);

            AddLabel(card, "倍速", 330, 160, 36);
            numSpeed = MakeNumeric(1, 1.8m, 0);
            numSpeed.DecimalPlaces = 1;
            numSpeed.Increment = 0.1m;
            numSpeed.Location = new Point(370, 156);
            numSpeed.Size = new Size(78, 24);
            card.Controls.Add(numSpeed);
            AddHint(card, "范围 1.0 – 1.8", 458, 160, 120);

            chkSound = MakeCheck(card, "静音播放", 72, 196, 110);
            chkCaptcha = MakeCheck(card, "自动过滑块", 206, 196, 120);
            chkHide = MakeCheck(card, "隐藏窗口", 352, 196, 110);
            chkDonate = MakeCheck(card, "显示赞赏码", 490, 196, 120);

            AddLabel(card, "课程链接", 18, 236, 76);
            AddHint(card, "一行一个，需以 https:// 开头", 100, 236, 300);

            chkGameGuard = MakeCheck(card, "游戏时自动暂停", 392, 234, 200);
            chkGameGuard.CheckedChanged += delegate
            {
                UpdateGameHint();
                if (!chkGameGuard.Checked)
                {
                    gameActive = false;
                    hotCount = 0;
                    coldCount = 0;
                }
            };

            boxUrls = new TextBox();
            boxUrls.Location = new Point(18, 262);
            boxUrls.Size = new Size(604, 232);
            boxUrls.Multiline = true;
            boxUrls.ScrollBars = ScrollBars.Vertical;
            boxUrls.AcceptsReturn = true;
            boxUrls.BorderStyle = BorderStyle.FixedSingle;
            boxUrls.Font = T.F(9.5f);
            boxUrls.BackColor = Color.White;
            boxUrls.ForeColor = T.Text;
            boxUrls.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            card.Controls.Add(boxUrls);

            Panel div = new Panel();
            div.Location = new Point(18, 510);
            div.Size = new Size(604, 1);
            div.BackColor = T.Line;
            div.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            card.Controls.Add(div);

            Label hintCred = AddHint(card, "密码以 Windows DPAPI 加密保存在 data\\cred.bin，config.ini 中不落明文",
                18, 520, 604);
            hintCred.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            btnStart = new FlatButton();
            btnStart.Text = "开始刷课";
            btnStart.Location = new Point(18, 546);
            btnStart.Size = new Size(140, 42);
            btnStart.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnStart.Click += delegate { StartStudy(); };
            card.Controls.Add(btnStart);

            btnStop = new FlatButton();
            btnStop.Text = "停止";
            btnStop.Fill = T.Soft;
            btnStop.HoverFill = Color.FromArgb(229, 231, 235);
            btnStop.TextColor = T.Text;
            btnStop.Location = new Point(170, 546);
            btnStop.Size = new Size(110, 42);
            btnStop.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnStop.Click += delegate { StopStudy(); };
            card.Controls.Add(btnStop);

            btnSave = new FlatButton();
            btnSave.Text = "保存配置";
            btnSave.Fill = T.Soft;
            btnSave.HoverFill = Color.FromArgb(229, 231, 235);
            btnSave.TextColor = T.Text;
            btnSave.Location = new Point(292, 546);
            btnSave.Size = new Size(110, 42);
            btnSave.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            btnSave.Click += delegate { SaveConfig(true); };
            card.Controls.Add(btnSave);
        }

        // ---------------- 状态卡片 ----------------
        private void BuildStatusCard()
        {
            Card card = statusCard;

            AddHint(card, "最近更新", 18, 74, 56);
            lblUpdated = AddValue(card, "—", 92, 74, 72);
            AddHint(card, "日志静默", 216, 74, 56);
            lblIdle = AddValue(card, "—", 286, 74, 150);

            AddHint(card, "当前课时", 18, 108, 56);
            lblLesson = AddValue(card, "—", 18, 130, 402);

            AddHint(card, "平台进度", 18, 164, 56);
            lblProgress = AddValue(card, "—", 18, 186, 60);
            lblProgress.AutoSize = false;
            lblProgress.Font = T.F(14f, FontStyle.Bold);
            lblProgress.ForeColor = T.Accent;
            lblProgress.TextAlign = ContentAlignment.BottomRight;
            lblProgress.Location = new Point(300, 172);
            lblProgress.Size = new Size(120, 30);
            lblProgress.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            bar = new Bar();
            bar.Location = new Point(18, 214);
            bar.Size = new Size(402, 8);
            bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(bar);

            AddHint(card, "已完成节数", 18, 236, 70);
            lblDone = AddValue(card, "—", 110, 236, 200);

            Panel div = new Panel();
            div.Location = new Point(18, 272);
            div.Size = new Size(402, 1);
            div.BackColor = T.Line;
            div.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(div);

            AddHint(card, "最近日志", 18, 284, 70);

            boxLog = new TextBox();
            boxLog.Location = new Point(18, 306);
            boxLog.Size = new Size(402, 254);
            boxLog.Multiline = true;
            boxLog.ReadOnly = true;
            boxLog.ScrollBars = ScrollBars.Vertical;
            boxLog.WordWrap = true;
            boxLog.BorderStyle = BorderStyle.FixedSingle;
            boxLog.BackColor = Color.FromArgb(250, 250, 251);
            boxLog.ForeColor = Color.FromArgb(55, 65, 81);
            boxLog.Font = new Font("Consolas", 8.5f);
            boxLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            card.Controls.Add(boxLog);

            hintTip = AddHint(card, "全屏游戏时会自动暂停提醒，不影响游戏", 18, 568, 402);
            hintTip.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        }

        // ---------------- 控件小工具 ----------------
        private NumericUpDown MakeNumeric(decimal min, decimal max, int decimals)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.DecimalPlaces = decimals;
            n.BorderStyle = BorderStyle.None;
            n.Font = T.F(9.5f);
            n.TextAlign = HorizontalAlignment.Center;
            n.BackColor = T.Soft;
            n.ForeColor = T.Text;
            return n;
        }

        private CheckBox MakeCheck(Control parent, string text, int x, int y, int w)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Location = new Point(x, y);
            c.AutoSize = true;
            c.Font = T.F(9f);
            c.ForeColor = T.Text;
            c.BackColor = Color.Transparent;
            c.FlatStyle = FlatStyle.Flat;
            parent.Controls.Add(c);
            return c;
        }

        private void AddLabel(Control parent, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = true;
            l.Font = T.F(9f);
            l.ForeColor = T.Text;
            l.BackColor = Color.Transparent;
            parent.Controls.Add(l);
        }

        private Label AddHint(Control parent, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = true;
            l.Font = T.F(8.5f);
            l.ForeColor = T.Sub;
            l.BackColor = Color.Transparent;
            parent.Controls.Add(l);
            return l;
        }

        private Label AddValue(Control parent, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = true;
            l.Font = T.F(9f);
            l.ForeColor = T.Text;
            l.BackColor = Color.Transparent;
            parent.Controls.Add(l);
            return l;
        }

        // ---------------- 配置读写 ----------------
        private void LoadConfig()
        {
            Config c = Config.Load(configPath);

            boxUser.Text = c.Username;
            boxPass.Text = cred.Load();

            // 旧版 config.ini 若残留明文密码，自动迁移到加密存储并清除明文
            if (!cred.Exists && c.PasswordInIni.Length > 0)
            {
                if (cred.Save(c.PasswordInIni))
                {
                    boxPass.Text = c.PasswordInIni;
                    c.PasswordInIni = "";
                    try { c.Save(configPath); } catch { }
                }
            }
            UpdateCredPill();
            cbDriver.SelectedItem = c.Driver;
            boxExe.Text = c.ExePath;

            numMaxTime.Value = Clamp(c.LimitMaxTime, 0, 600);
            numSpeed.Value = ClampDecimal(c.LimitSpeed);

            chkSound.Checked = c.SoundOff;
            chkCaptcha.Checked = c.AutoCaptcha;
            chkHide.Checked = c.HideWindow;
            chkDonate.Checked = c.Donate;
            chkGameGuard.Checked = c.GamePause;

            if (c.SplitPos >= MinConfig) leftWidth = c.SplitPos;

            StringBuilder sb = new StringBuilder();
            foreach (string u in c.Urls) sb.AppendLine(u);
            boxUrls.Text = sb.ToString().TrimEnd('\r', '\n');
        }

        private decimal Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private decimal ClampDecimal(double v)
        {
            if (v < 1.0) v = 1.0;
            if (v > 1.8) v = 1.8;
            return Math.Round((decimal)v, 1);
        }

        private void UpdateCredPill()
        {
            if (cred.Exists)
            {
                credPill.Set("已加密", Color.FromArgb(220, 252, 231), T.Ok);
            }
            else
            {
                credPill.Set("未保存", T.Soft, T.Sub);
            }
        }

        // 返回 false 表示校验失败
        private bool SaveConfig(bool notify)
        {
            string[] rows = boxUrls.Text.Replace("\r\n", "\n").Split('\n');
            Config c = new Config();
            c.Username = boxUser.Text.Trim();
            c.Driver = cbDriver.SelectedItem == null ? "Edge" : cbDriver.SelectedItem.ToString();
            c.ExePath = boxExe.Text.Trim();
            c.AutoCaptcha = chkCaptcha.Checked;
            c.HideWindow = chkHide.Checked;
            c.Donate = chkDonate.Checked;
            c.SoundOff = chkSound.Checked;
            c.LimitMaxTime = (int)numMaxTime.Value;
            c.LimitSpeed = (double)numSpeed.Value;
            c.GamePause = chkGameGuard.Checked;
            c.SplitPos = leftWidth;

            foreach (string row in rows)
            {
                string u = row.Trim();
                if (u.Length == 0) continue;
                if (!u.StartsWith("http://") && !u.StartsWith("https://"))
                {
                    MessageBox.Show(this, "课程链接必须以 http:// 或 https:// 开头：\r\n\r\n" + u,
                        "链接格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                c.Urls.Add(u);
            }

            bool ok = cred.Save(boxPass.Text.Trim());
            string err = null;
            try { c.Save(configPath); }
            catch (Exception ex) { err = ex.Message; }

            UpdateCredPill();

            if (err != null)
            {
                MessageBox.Show(this, "保存配置失败：" + err, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (!ok)
            {
                MessageBox.Show(this, "密码加密保存失败，请检查 data 目录权限。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (notify)
            {
                MessageBox.Show(this, "配置已保存。密码以 DPAPI 加密存储，config.ini 中不含明文。",
                    "已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }

        // ---------------- 运行控制 ----------------
        private void StartStudy()
        {
            if (runner.IsRunning)
            {
                MessageBox.Show(this, "Autovisor 已在运行中。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!SaveConfig(false)) return;
            if (boxUrls.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "请至少填写一个课程链接。", "缺少课程", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string err = runner.Start(boxPass.Text.Trim());
            if (err != null)
            {
                MessageBox.Show(this, err, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            RefreshStatus();
        }

        private void StopStudy()
        {
            if (!runner.IsRunning)
            {
                MessageBox.Show(this, "Autovisor 当前没有运行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            runner.Stop();
            System.Threading.Thread.Sleep(300);
            RefreshStatus();
        }

        // ---------------- 状态刷新 ----------------
        private void RefreshStatus()
        {
            StudySnapshot s = runner.Snapshot();

            if (gameActive)
            {
                pill.Set("游戏暂停", Color.FromArgb(254, 243, 199), T.Warn);
            }
            else if (s.Running)
            {
                pill.Set("刷课中", Color.FromArgb(220, 252, 231), T.Ok);
            }
            else if (s.NeedAttention)
            {
                pill.Set("待处理", Color.FromArgb(254, 226, 226), T.Err);
            }
            else
            {
                pill.Set("未运行", T.Soft, T.Sub);
            }

            lblUpdated.Text = s.UpdatedText;
            lblLesson.Text = s.LessonText;
            lblDone.Text = s.Total > 0 ? (s.Done + " / " + s.Total + " 节") : (s.Done + " 节");

            if (s.Progress >= 0)
            {
                bar.Value = s.Progress;
                lblProgress.Text = s.Progress + "%";
            }
            else
            {
                bar.Value = 0;
                lblProgress.Text = "—";
            }

            if (s.IdleMinutes < 0)
            {
                lblIdle.Text = "—";
                lblIdle.ForeColor = T.Sub;
            }
            else
            {
                lblIdle.Text = s.IdleMinutes + " 分钟前";
                lblIdle.ForeColor = s.IdleMinutes >= StuckMinutes ? T.Warn : T.Sub;
            }

            if (gameActive)
            {
                attention.Text = "检测到游戏 " + gameDetail + "，已暂停刷课";
                attention.Visible = true;
            }
            else if (s.NeedAttention)
            {
                attention.Text = s.AttentionText;
                attention.Visible = true;
                if (!lastAttention && !guardPaused)
                {
                    Flash();
                }
            }
            else
            {
                attention.Visible = false;
            }
            lastAttention = s.NeedAttention;

            tray.Visible = s.Running || gameActive;
            CheckStuck(s);

            if (s.Tail.Length > 0 && boxLog.Text != s.Tail)
            {
                boxLog.Text = s.Tail;
                boxLog.SelectionStart = boxLog.Text.Length;
                boxLog.ScrollToCaret();
            }
        }

        // 疑似卡死：日志长时间无新行，用系统通知在右下角提醒
        private void CheckStuck(StudySnapshot s)
        {
            bool stuck = s.Running && !gameActive && !guardPaused && s.IdleMinutes >= StuckMinutes;

            if (!stuck)
            {
                stuckNotified = false;
                return;
            }

            if (stuckNotified)
            {
                if ((DateTime.Now - lastStuckNotify).TotalMinutes < StuckRenotifyMin) return;
            }

            stuckNotified = true;
            lastStuckNotify = DateTime.Now;

            string body = "日志已 " + Math.Round(s.IdleMinutes) + " 分钟没有更新，请检查面板或浏览器窗口。";
            if (s.LessonText != null && s.LessonText != "—") body += Environment.NewLine + "当前：" + s.LessonText;

            ShowToast("刷课可能卡住了", body);
            WriteEventLog("疑似卡死：日志静默 " + s.IdleMinutes + " 分钟，已弹出系统通知");
        }

        // 通过 Shell_NotifyIcon 的托盘气泡，由 Windows 在右下角以系统通知呈现
        private void ShowToast(string title, string body)
        {
            try
            {
                if (tray == null) return;
                tray.Visible = true;
                tray.ShowBalloonTip(10000, title, body, ToolTipIcon.Warning);
            }
            catch { }
        }

        private void RestorePanel()
        {
            try
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show();
                Activate();
                TopMost = true;
                TopMost = false;
                Focus();
            }
            catch { }
        }

        // 记录是否处于全屏游戏，游戏期间不打扰
        private void GuardTick()
        {
            guardPaused = GameGuard.IsFullScreen();
        }

        // ---------------- 游戏检测（GPU 3D 占用） ----------------
        private void GameTick()
        {
            if (!chkGameGuard.Checked)
            {
                return;
            }
            if (!probe.Available)
            {
                hintTip.Text = "游戏检测不可用：当前系统未提供 GPU 计数器";
                hintTip.ForeColor = T.Warn;
                return;
            }

            GameHit hit = probe.Sample(25.0);

            if (hit != null)
            {
                hotCount++;
                coldCount = 0;
                gameDetail = hit.Name + ".exe " + ((int)Math.Round(hit.Util)) + "%";
            }
            else
            {
                hotCount = 0;
                coldCount++;
            }

            if (!gameActive && hotCount >= 3)
            {
                gameActive = true;
                OnGameStart();
            }
            else if (gameActive && coldCount >= 12)
            {
                gameActive = false;
                OnGameEnd();
            }

            UpdateGameHint();
        }

        private void OnGameStart()
        {
            WriteEventLog("检测到游戏 " + gameDetail + "，暂停刷课");
            if (runner.IsRunning)
            {
                runner.Stop();
                pausedByGame = true;
            }
            RefreshStatus();
        }

        private void OnGameEnd()
        {
            bool resume = pausedByGame;
            pausedByGame = false;
            WriteEventLog("游戏结束，GPU 已空闲");

            if (resume && chkGameGuard.Checked)
            {
                string err = runner.Start(boxPass.Text.Trim());
                WriteEventLog(err == null ? "已自动恢复刷课" : ("自动恢复失败：" + err));
            }
            RefreshStatus();
        }

        private void UpdateGameHint()
        {
            string text;
            Color color;

            if (!chkGameGuard.Checked)
            {
                text = "游戏检测已关闭";
                color = T.Sub;
            }
            else if (!probe.Available)
            {
                text = "游戏检测不可用：当前系统未提供 GPU 计数器";
                color = T.Warn;
            }
            else if (gameActive)
            {
                text = "游戏检测：" + gameDetail + "，刷课已暂停";
                color = T.Warn;
            }
            else
            {
                text = "游戏检测：空闲（≥25% 持续 15 秒判定为游戏）";
                color = T.Sub;
            }

            if (hintTip.Text != text)
            {
                hintTip.Text = text;
                hintTip.ForeColor = color;
            }
        }

        private void WriteEventLog(string msg)
        {
            try
            {
                string dir = Path.Combine(root, "logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + msg + Environment.NewLine;
                File.AppendAllText(Path.Combine(dir, "panel.log"), line, Encoding.UTF8);
            }
            catch { }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (runner.IsRunning)
            {
                DialogResult r = MessageBox.Show(this,
                    "退出面板会停止刷课，确定退出吗？", "确认退出",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                runner.Stop();
            }
            tickStatus.Stop();
            tickGuard.Stop();
            tickGame.Stop();

            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
        }

        // ---------------- 窗口闪烁提示 ----------------
        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        private void Flash()
        {
            FLASHWINFO fi = new FLASHWINFO();
            fi.cbSize = (uint)Marshal.SizeOf(fi);
            fi.hwnd = Handle;
            fi.dwFlags = 0x00000002 | 0x00000001;
            fi.uCount = 3;
            fi.dwTimeout = 0;
            FlashWindowEx(ref fi);
        }
    }
}
