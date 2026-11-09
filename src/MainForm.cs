using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AutovisorPanel
{
    internal partial class MainForm : Form
    {
        private readonly string root;
        private readonly string configPath;
        private readonly CredentialStore cred;
        private readonly StudyRunner runner;
        private readonly object runtimeGate = new object();
        private StudySnapshot latestStatus;
        private bool refreshBusy;
        private bool operationBusy;
        private bool gameProbeBusy;
        private bool initialPollDone;
        private bool closing;
        private bool allowClose;
        private int operationVersion;

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
        private Label lblCourseTitle;
        private Label lblChapterTitle;
        private Pill lessonNumber;
        private Label lblLesson;
        private Label lblProgress;
        private Label lblPlayerTime;
        private Label lblVideoRemaining;
        private Bar bar;
        private CompletionTile courseCompletion;
        private CompletionTile chapterCompletion;
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

        private bool stuckNotified;
        private DateTime lastStuckNotify = DateTime.MinValue;

        private const double StuckMinutes = LogTail.StuckMinutes;
        private const int StuckRenotifyMin = 15;     // 持续卡死时的重复提醒间隔

        private int savedSplitPos = 640;

        public MainForm(string root)
        {
            this.root = root;
            configPath = Path.Combine(root, "config.ini");
            cred = new CredentialStore(Path.Combine(root, "data", "cred.bin"));
            runner = new StudyRunner(root);

            Text = "Autovisor 面板";
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            BackColor = T.Bg;
            Font = T.F(11f);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;

            SetInitialBounds(Screen.FromPoint(Cursor.Position).WorkingArea);
            BuildWorkspace();

            tickStatus = new Timer();
            tickStatus.Interval = 3000;
            tickStatus.Tick += delegate { RefreshStatus(); };

            tickGuard = new Timer();
            tickGuard.Interval = 15000;
            tickGuard.Tick += delegate { GuardTick(); };

            tickGame = new Timer();
            tickGame.Interval = 5000;
            tickGame.Tick += delegate { GameTick(); };

            BuildTray();

            Load += delegate
            {
                LoadConfig();
                StartTray();
                UpdateButtons();
                RefreshStatus();
                UpdateGameHint();
                tickStatus.Start();
                tickGuard.Start();
                tickGame.Start();
            };

            FormClosing += OnClosing;
        }

        private void SetInitialBounds(Rectangle area)
        {
            Size desired = SizeFromClientSize(new Size(T.Px(1100), T.Px(700)));
            Size minimum = SizeFromClientSize(new Size(T.Px(740), T.Px(430)));
            MinimumSize = new Size(Math.Min(minimum.Width, area.Width), Math.Min(minimum.Height, area.Height));
            Size = new Size(Math.Min(desired.Width, area.Width), Math.Min(desired.Height, area.Height));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
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
            savedSplitPos = c.SplitPos;

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
        private Config ReadSettings()
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
            c.SplitPos = savedSplitPos;

            foreach (string row in rows)
            {
                string u = row.Trim();
                if (u.Length == 0) continue;
                if (!u.StartsWith("http://") && !u.StartsWith("https://"))
                {
                    MessageBox.Show(this, "课程链接必须以 http:// 或 https:// 开头：\r\n\r\n" + u,
                        "链接格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                c.Urls.Add(u);
            }

            return c;
        }

        private string WriteSettings(Config config, string password)
        {
            if (!cred.Save(password)) return "密码加密保存失败，请检查 data 目录权限。";
            config.Save(configPath);
            return null;
        }

        private async void SaveSettings()
        {
            if (operationBusy || closing) return;
            Config config = ReadSettings();
            if (config == null) return;
            string password = boxPass.Text.Trim();
            string error = await RunOperation("正在保存设置…", delegate { return WriteSettings(config, password); });
            if (closing || IsDisposed) return;
            UpdateCredPill();
            if (error == null) SetText(attention, "设置已保存");
            else ShowOperationError("保存失败", error);
        }

        private async void StartStudy()
        {
            if (operationBusy || closing) return;
            Config config = ReadSettings();
            if (config == null) return;
            if (config.Urls.Count == 0)
            {
                SelectPage(2);
                ShowOperationError("缺少课程", "请至少填写一个课程链接。");
                boxUrls.Focus();
                return;
            }
            string password = boxPass.Text.Trim();
            string error = await RunOperation("正在启动…", delegate
            {
                if (runner.IsRunning) return "Autovisor 已在运行中。";
                string saveError = WriteSettings(config, password);
                return saveError ?? runner.Start(password);
            });
            if (closing || IsDisposed) return;
            UpdateCredPill();
            if (error != null) ShowOperationError("启动失败", error);
            else SelectPage(0);
        }

        private async void StopStudy()
        {
            if (operationBusy || closing) return;
            pausedByGame = false;
            string error = await RunOperation("正在停止…", delegate
            {
                if (runner.IsRunning) runner.Stop();
                return runner.IsRunning ? "仍检测到 Autovisor 进程。" : null;
            });
            if (error != null && !closing && !IsDisposed) ShowOperationError("停止失败", error);
        }

        private void ShowOperationError(string title, string message)
        {
            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private async Task<string> RunOperation(string status, Func<string> work)
        {
            operationBusy = true;
            operationVersion++;
            SetText(attention, status);
            UpdateButtons();
            try
            {
                return await Task.Run(delegate
                {
                    lock (runtimeGate) return work();
                });
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                operationBusy = false;
                if (!closing && !IsDisposed)
                {
                    UpdateButtons();
                    RefreshStatus();
                }
            }
        }

        private void UpdateButtons()
        {
            bool running = latestStatus != null && latestStatus.Running;
            btnStart.Enabled = !operationBusy && !closing && !running && !gameActive;
            btnStop.Enabled = !operationBusy && !closing && (running || pausedByGame);
            btnSave.Enabled = !operationBusy && !closing;
            UpdateTrayTooltip();
        }

        private async void RefreshStatus()
        {
            if (refreshBusy || operationBusy || closing || IsDisposed) return;
            refreshBusy = true;
            int version = operationVersion;
            try
            {
                StudySnapshot snapshot = await Task.Run(delegate
                {
                    lock (runtimeGate)
                    {
                        StudySnapshot result = runner.Snapshot();
                        if (!initialPollDone && !result.Running) StudyRunner.CleanTempConfigs();
                        initialPollDone = true;
                        return result;
                    }
                });
                if (closing || IsDisposed || operationBusy || version != operationVersion) return;
                latestStatus = snapshot;
                RenderHeader(snapshot);
                RenderActivePage(snapshot);
            }
            catch (Exception ex)
            {
                if (!closing && !IsDisposed)
                    SetText(attention, "状态暂时不可用：" + ex.Message);
            }
            finally
            {
                refreshBusy = false;
                if (!closing && !IsDisposed && version != operationVersion) RefreshStatus();
            }
        }

        private void RenderHeader(StudySnapshot s)
        {
            if (gameActive)
                pill.Set("游戏暂停", Color.FromArgb(254, 243, 199), T.Warn);
            else if (s.NeedAttention)
                pill.Set("待处理", Color.FromArgb(254, 226, 226), T.Err);
            else if (s.Running)
                pill.Set("学习中", Color.FromArgb(220, 252, 231), T.Ok);
            else
                pill.Set("未运行", T.Soft, T.Sub);

            if (gameActive)
                SetText(attention, "游戏暂停 · " + gameDetail);
            else if (s.NeedAttention)
            {
                SetText(attention, s.AttentionText);
                if (!lastAttention && !guardPaused) Flash();
            }
            else
                SetText(attention, "");

            lastAttention = s.NeedAttention;
            CheckStuck(s);
            UpdateButtons();
        }

        private void RenderActivePage(StudySnapshot s)
        {
            if (activePage == 1)
            {
                catalogView.Render(s.Catalog);
                return;
            }
            if (activePage == 3)
            {
                if (SetText(boxLog, s.Tail))
                {
                    boxLog.SelectionStart = boxLog.Text.Length;
                    boxLog.ScrollToCaret();
                }
                return;
            }
            if (activePage != 0) return;

            LearningOverview overview = s.Overview ?? LearningOverview.From(s);
            SetText(lblUpdated, s.UpdatedText);
            SetText(lblCourseTitle, string.IsNullOrWhiteSpace(overview.CourseTitle) ? "等待课程名称" : overview.CourseTitle);
            SetText(lblChapterTitle, string.IsNullOrWhiteSpace(overview.ChapterTitle) ? "章节信息待同步" : overview.ChapterTitle);
            lessonNumber.Set(string.IsNullOrEmpty(overview.LessonNumber) ? "小节" : overview.LessonNumber,
                Color.FromArgb(235, 241, 255), T.Accent);
            SetText(lblLesson, string.IsNullOrWhiteSpace(overview.LessonTitle) || overview.LessonTitle == "—"
                ? "等待课程开始" : overview.LessonTitle);
            courseCompletion.Render(overview.Course);
            chapterCompletion.Render(overview.Chapter);
            bool hasPlayerTime = s.PlayerSeconds >= 0 && s.DurationSeconds > 0;
            SetText(lblPlayerTime, hasPlayerTime
                ? "播放器 " + FormatPlaybackTime(s.PlayerSeconds) + " / " + FormatPlaybackTime(s.DurationSeconds) : "播放器 —");
            SetText(lblVideoRemaining, hasPlayerTime
                ? "视频剩余 " + FormatPlaybackTime(Math.Max(0, s.DurationSeconds - s.PlayerSeconds)) : "视频剩余 —");
            bar.Value = Math.Max(0, s.Progress);
            SetText(lblProgress, s.Progress >= 0 ? s.Progress + "%" : "—");
            SetText(lblIdle, !s.Running || s.IdleMinutes < 0
                ? "未计时" : FormatPlaybackTime((int)(s.IdleMinutes * 60)));
            lblIdle.ForeColor = s.Running && s.IdleMinutes >= StuckMinutes ? T.Warn : T.Sub;
        }

        private static bool SetText(Control control, string value)
        {
            if (control.Text == value) return false;
            control.Text = value;
            return true;
        }

        private static string FormatPlaybackTime(int seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1
                ? ((int)time.TotalHours) + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00")
                : time.Minutes + ":" + time.Seconds.ToString("00");
        }

        // 日志长时间无新行时，提示检查播放暂停或安全验证。
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

            ShowToast("播放可能暂停或等待验证", body);
            Task.Run(delegate { WriteEventLog("播放可能暂停或等待验证：日志静默 " + s.IdleMinutes.ToString("0.#") + " 分钟，已弹出系统通知"); });
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
        private async void GameTick()
        {
            if (closing || operationBusy || gameProbeBusy || !chkGameGuard.Checked) return;
            gameProbeBusy = true;
            try
            {
                GameHit hit = await Task.Run(delegate { return probe.Sample(25.0); });
                if (closing || IsDisposed || operationBusy || !chkGameGuard.Checked) return;
                if (!probe.Available)
                {
                    UpdateGameHint();
                    return;
                }

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
                    await OnGameStart();
                }
                else if (gameActive && coldCount >= 12)
                {
                    gameActive = false;
                    await OnGameEnd();
                }
                if (!closing && !IsDisposed) UpdateGameHint();
            }
            finally
            {
                gameProbeBusy = false;
            }
        }

        private async Task OnGameStart()
        {
            bool paused = false;
            string detail = gameDetail;
            string error = await RunOperation("检测到游戏，正在暂停…", delegate
            {
                WriteEventLog("检测到游戏 " + detail + "，暂停刷课");
                if (!runner.IsRunning) return null;
                runner.Stop();
                paused = !runner.IsRunning;
                return paused ? null : "仍检测到 Autovisor 进程。";
            });
            if (closing || IsDisposed) return;
            pausedByGame = paused && chkGameGuard.Checked;
            UpdateButtons();
            if (error != null) SetText(attention, "游戏暂停失败：" + error);
        }

        private async Task OnGameEnd()
        {
            bool resume = pausedByGame && chkGameGuard.Checked;
            pausedByGame = false;
            string password = boxPass.Text.Trim();
            string error = await RunOperation(resume ? "正在恢复学习…" : "游戏已结束", delegate
            {
                WriteEventLog("游戏结束，GPU 已空闲");
                if (!resume || runner.IsRunning) return null;
                string err = runner.Start(password);
                WriteEventLog(err == null ? "已自动恢复刷课" : ("自动恢复失败：" + err));
                return err;
            });
            if (error != null && !closing && !IsDisposed) SetText(attention, "自动恢复失败：" + error);
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

        private async void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (allowClose)
            {
                tickStatus.Dispose();
                tickGuard.Dispose();
                tickGame.Dispose();
                DisposeTray();
                return;
            }
            e.Cancel = true;
            if (closing || operationBusy) return;
            closing = true;
            UpdateButtons();
            try
            {
                bool running = await Task.Run(delegate
                {
                    lock (runtimeGate) return runner.IsRunning;
                });
                if (running && MessageBox.Show(this, "退出面板会停止学习，确定退出吗？", "确认退出",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                SetText(attention, "正在退出…");
                await Task.Run(delegate
                {
                    lock (runtimeGate)
                    {
                        if (running) runner.Stop();
                        runner.CloseCatalog();
                    }
                });
                allowClose = true;
                Close();
            }
            catch (Exception ex)
            {
                if (!IsDisposed) ShowOperationError("退出失败", ex.Message);
            }
            finally
            {
                if (!allowClose)
                {
                    closing = false;
                    UpdateButtons();
                    RefreshStatus();
                }
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
