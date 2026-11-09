using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AutovisorPanel
{
    // ---------------- 凭据：DPAPI 加密，仅当前用户可解密 ----------------
    internal class CredentialStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AutovisorPanel.cred.v1");
        private readonly string path;

        public CredentialStore(string path)
        {
            this.path = path;
        }

        public bool Exists
        {
            get { return File.Exists(path); }
        }

        public string Load()
        {
            try
            {
                if (!File.Exists(path)) return "";
                byte[] raw = File.ReadAllBytes(path);
                byte[] data = ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return "";
            }
        }

        public bool Save(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value))
                {
                    if (File.Exists(path)) File.Delete(path);
                    return true;
                }
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path, data);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // ---------------- 配置 ----------------  config.ini 中永不出现密码明文
    internal class Config
    {
        public string Username = "";
        public string PasswordInIni = "";
        public string Driver = "Edge";
        public string ExePath = "";
        public bool AutoCaptcha = true;
        public bool HideWindow = false;
        public bool Donate = true;
        public bool SoundOff = true;
        public int LimitMaxTime = 30;
        public double LimitSpeed = 1.0;
        public bool GamePause = true;
        public int SplitPos = 640;
        public List<string> Urls = new List<string>();

        public static Config Load(string path)
        {
            Config c = new Config();
            if (!File.Exists(path)) return c;

            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch { return c; }

            string section = "";
            foreach (string raw in lines)
            {
                string t = raw.Trim();
                if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("#")) continue;

                if (t.StartsWith("[") && t.EndsWith("]") && t.Length > 2)
                {
                    section = t.Substring(1, t.Length - 2).Trim();
                    continue;
                }

                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                string key = t.Substring(0, eq).Trim();
                string val = t.Substring(eq + 1).Trim();

                if (section == "user-account")
                {
                    if (key == "username") c.Username = val;
                    else if (key == "password") c.PasswordInIni = val;
                }
                else if (section == "browser-option")
                {
                    if (key == "driver") c.Driver = val.Length > 0 ? val : "Edge";
                    else if (key == "EXE_PATH") c.ExePath = val;
                }
                else if (section == "script-option")
                {
                    if (key == "enableAutoCaptcha") c.AutoCaptcha = IsTrue(val, true);
                    else if (key == "enableHideWindow") c.HideWindow = IsTrue(val, false);
                    else if (key == "showDonateCode") c.Donate = IsTrue(val, true);
                }
                else if (section == "course-option")
                {
                    if (key == "soundOff") c.SoundOff = IsTrue(val, true);
                    else if (key == "limitMaxTime") { int v; if (int.TryParse(val, out v)) c.LimitMaxTime = v; }
                    else if (key == "limitSpeed") { double v; if (double.TryParse(val, out v)) c.LimitSpeed = v; }
                }
                else if (section == "panel")
                {
                    if (key == "gamePause") c.GamePause = IsTrue(val, true);
                    else if (key == "splitPos") { int v; if (int.TryParse(val, out v)) c.SplitPos = v; }
                }
                else if (section == "course-url")
                {
                    if (key.StartsWith("URL") && val.Length > 0 && val.StartsWith("http")) c.Urls.Add(val);
                }
            }

            if (c.LimitMaxTime < 0) c.LimitMaxTime = 0;
            if (c.LimitSpeed < 1.0) c.LimitSpeed = 1.0;
            if (c.LimitSpeed > 1.8) c.LimitSpeed = 1.8;
            return c;
        }

        private static bool IsTrue(string s, bool def)
        {
            if (s.Length == 0) return def;
            s = s.ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "on") return true;
            if (s == "false" || s == "0" || s == "no" || s == "off") return false;
            return def;
        }

        public void Save(string path)
        {
            Save(path, null);
        }

        public static void SaveSwitch(string path, string section, string key, bool enabled)
        {
            List<string> lines = File.Exists(path)
                ? new List<string>(File.ReadAllLines(path, Encoding.UTF8)) : new List<string>();
            int sectionEnd = -1;
            int option = -1;
            bool inside = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                    inside = line.Substring(1, line.Length - 2).Trim() == section;
                if (!inside) continue;
                sectionEnd = i + 1;
                int eq = line.IndexOf('=');
                if (eq > 0 && line.Substring(0, eq).Trim() == key) option = i;
            }

            string value = key + " = " + (enabled ? "True" : "False");
            if (option >= 0) lines[option] = value;
            else if (sectionEnd >= 0) lines.Insert(sectionEnd, value);
            else
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Length > 0) lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(value);
            }

            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        // password 传 null 表示不写入密码（面板常态）
        public void Save(string path, string passwordForRun)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[user-account]");
            sb.AppendLine(";账号密码。密码由面板以 DPAPI 加密保存在 data\\cred.bin，此处不落明文");
            sb.AppendLine("username =" + Username);
            sb.AppendLine("password =");
            sb.AppendLine();
            sb.AppendLine("[browser-option]");
            sb.AppendLine(";配置浏览器,可选 Edge 或 Chrome");
            sb.AppendLine("driver = " + Driver);
            sb.AppendLine(";指定浏览器所在路径,不填则使用默认路径");
            sb.AppendLine("EXE_PATH =" + ExePath);
            sb.AppendLine();
            sb.AppendLine("[script-option]");
            sb.AppendLine(";是否自动过登录时的滑块验证");
            sb.AppendLine("enableAutoCaptcha = " + (AutoCaptcha ? "True" : "False"));
            sb.AppendLine(";是否自动隐藏浏览器窗口");
            sb.AppendLine("enableHideWindow = " + (HideWindow ? "True" : "False"));
            sb.AppendLine(";是否展示赞赏码");
            sb.AppendLine("showDonateCode = " + (Donate ? "True" : "False"));
            sb.AppendLine();
            sb.AppendLine("[course-option]");
            sb.AppendLine(";限制每门课程刷课时长/min (不限制:0)");
            sb.AppendLine("limitMaxTime = " + LimitMaxTime);
            sb.AppendLine(";限定播放倍速 (最高:1.8)");
            sb.AppendLine("limitSpeed = " + LimitSpeed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine(";设置是否静音播放");
            sb.AppendLine("soundOff = " + (SoundOff ? "True" : "False"));
            sb.AppendLine();
            sb.AppendLine("[course-url]");
            sb.AppendLine(";配置要学习的课程链接,支持(智慧)共享课");
            sb.AppendLine(";可继续添加多个课程链接");
            if (Urls.Count == 0)
            {
                sb.AppendLine("URL1 =");
            }
            else
            {
                for (int i = 0; i < Urls.Count; i++)
                {
                    sb.AppendLine("URL" + (i + 1) + " = " + Urls[i]);
                }
            }
            sb.AppendLine();
            sb.AppendLine("[panel]");
            sb.AppendLine(";面板自身的选项，上游程序忽略此段");
            sb.AppendLine(";检测到游戏时自动暂停刷课（依据 GPU 3D 占用）");
            sb.AppendLine("gamePause = " + (GamePause ? "True" : "False"));
            sb.AppendLine(";配置与状态两栏的分隔位置（像素）");
            sb.AppendLine("splitPos = " + SplitPos);

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }

    // ---------------- 日志解析 ----------------
    internal class StudySnapshot
    {
        public bool Running;
        public string CourseTitle = "";
        public string LessonText = "—";
        public int Progress = -1;
        public int PlayerSeconds = -1;
        public int DurationSeconds = -1;
        public int Done = -1;
        public int Total;
        public string UpdatedText = "—";
        public double IdleMinutes = -1;
        public bool NeedAttention;
        public string AttentionText = "";
        public string Tail = "";
        public string LogFile = "";
        public CourseCatalog Catalog;
        public LearningOverview Overview;
    }

    // 增量读取日志：首轮读全量，之后只读新增部分，避免每 3 秒重复解析整个文件
    internal class LogTail
    {
        private static readonly Regex CatalogCount = new Regex(@"Get to-learn class: ([0-9]{1,9}) / ([0-9]{1,9})\s*$");
        private static readonly Regex CourseName = new Regex(@"当前课程 \| 名称=(.+)$|当前课程:<<(.+?)>>");
        private static readonly Regex LessonStart = new Regex(@"课时开始 \| 序号=([0-9]+/[0-9]+) 标题=(.+) 平台进度=([0-9]{1,3})%");
        private static readonly Regex LessonResult = new Regex(@"课时结果 \| 序号=([0-9]+/[0-9]+) (.*)");
        private static readonly Regex PlatformProgress = new Regex(@" 平台进度=([0-9]{1,3})%(?=\s|$)");
        private static readonly Regex PlayerTime = new Regex(@" 播放器=([0-9]{1,9})s 总时长=([0-9]{1,9})s(?=\s|$)");
        private static readonly Regex LegacyCompletion = new Regex(@"(?:^| )完成=True(?:\s|$)");

        private string filePath = "";
        private long offset;
        private readonly HashSet<string> completed = new HashSet<string>();
        private int doneAtStart = -1;
        private int total;
        private string courseTitle = "";
        private string lessonPosition = "";
        private string lessonText = "—";
        private int progress = -1;
        private int playerSeconds = -1;
        private int durationSeconds = -1;
        private bool verifyPending;
        private readonly Queue<string> tail = new Queue<string>();
        private readonly Func<DateTime> utcNow;
        private DateTime? runStartedUtc;
        private DateTime lastActivityUtc;

        public const double StuckMinutes = 10;

        public LogTail(Func<DateTime> utcNow = null)
        {
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public void StartRun(DateTime startedUtc)
        {
            runStartedUtc = startedUtc;
            lastActivityUtc = startedUtc;
            verifyPending = false;
        }

        private void EndRun()
        {
            runStartedUtc = null;
            lastActivityUtc = DateTime.MinValue;
            verifyPending = false;
        }

        public static string FindLatest(string logsDir)
        {
            if (!Directory.Exists(logsDir)) return null;
            string[] files = Directory.GetFiles(logsDir, "Log*.txt");
            if (files.Length == 0) return null;

            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string f in files)
            {
                DateTime t = File.GetLastWriteTimeUtc(f);
                if (t > bestTime) { bestTime = t; best = f; }
            }
            return best;
        }

        private void Reset()
        {
            offset = 0;
            ResetCourse();
            tail.Clear();
        }

        private void ResetCourse()
        {
            completed.Clear();
            doneAtStart = -1;
            total = 0;
            courseTitle = "";
            lessonPosition = "";
            lessonText = "—";
            progress = -1;
            playerSeconds = -1;
            durationSeconds = -1;
            verifyPending = false;
        }

        public StudySnapshot Poll(string logsDir, bool running)
        {
            StudySnapshot s = new StudySnapshot();
            s.Running = running;
            DateTime now = utcNow();
            if (!running) EndRun();
            else if (!runStartedUtc.HasValue) StartRun(now);

            string latest = FindLatest(logsDir);
            if (latest == null)
            {
                UpdateSilence(s, now, null);
                return s;
            }
            s.LogFile = latest;

            if (latest != filePath)
            {
                filePath = latest;
                Reset();
            }

            try
            {
                ReadAppend();
            }
            catch
            {
                UpdateSilence(s, now, null);
                return s;
            }

            s.LessonText = lessonText;
            s.CourseTitle = courseTitle;
            s.Total = total;
            s.Done = doneAtStart < 0 ? -1 : doneAtStart + Math.Min(completed.Count, total - doneAtStart);
            s.Progress = progress;
            s.PlayerSeconds = playerSeconds;
            s.DurationSeconds = durationSeconds;

            DateTime wrote = File.GetLastWriteTimeUtc(filePath);
            s.UpdatedText = wrote.ToLocalTime().ToString("HH:mm:ss");
            UpdateSilence(s, now, wrote);
            s.Tail = string.Join(Environment.NewLine, tail.ToArray());
            return s;
        }

        private void UpdateSilence(StudySnapshot s, DateTime now, DateTime? wrote)
        {
            if (!runStartedUtc.HasValue) return;

            bool fromCurrentRun = wrote.HasValue && wrote.Value >= runStartedUtc.Value;
            if (fromCurrentRun && wrote.Value > lastActivityUtc)
                lastActivityUtc = wrote.Value;
            s.IdleMinutes = Math.Max(0, (now - lastActivityUtc).TotalMinutes);

            if (fromCurrentRun && verifyPending)
            {
                s.NeedAttention = true;
                s.AttentionText = "检测到安全验证，需要人工处理";
            }
            else if (s.IdleMinutes >= StuckMinutes)
            {
                s.NeedAttention = true;
                s.AttentionText = "日志已静默 " + s.IdleMinutes.ToString("0.#") + " 分钟，可能暂停或等待验证";
            }
        }

        private void ReadAppend()
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length < offset)
                {
                    Reset();
                }
                if (fs.Length <= offset) return;

                fs.Position = offset;
                byte[] buffer = new byte[fs.Length - offset];
                int read = fs.Read(buffer, 0, buffer.Length);

                // 只消费到最后一个完整换行，避免截断多字节字符或半行
                int lastBreak = -1;
                for (int i = read - 1; i >= 0; i--)
                {
                    if (buffer[i] == (byte)'\n') { lastBreak = i; break; }
                }
                if (lastBreak < 0) return;

                string chunk = Encoding.UTF8.GetString(buffer, 0, lastBreak);
                offset += lastBreak + 1;
                bool fromCurrentRun = runStartedUtc.HasValue && File.GetLastWriteTimeUtc(filePath) >= runStartedUtc.Value;
                Consume(chunk.Replace("\r\n", "\n").Split('\n'), fromCurrentRun);
            }
        }

        private void Consume(string[] lines, bool fromCurrentRun)
        {
            foreach (string line in lines)
            {
                if (line.Length == 0) continue;

                ConsumeProgress(line);

                if (fromCurrentRun)
                {
                    if (line.IndexOf("检测到安全验证", StringComparison.Ordinal) >= 0) verifyPending = true;
                    if (line.IndexOf("安全验证已完成", StringComparison.Ordinal) >= 0) verifyPending = false;
                }

                tail.Enqueue(line);
                while (tail.Count > 40) tail.Dequeue();
            }
        }

        private static int ReadPercent(Match match)
        {
            if (!match.Success) return -1;
            int value = int.Parse(match.Groups[1].Value);
            return value <= 100 ? value : -1;
        }

        private void ConsumeProgress(string line)
        {
            if (line.IndexOf("课程目录 | 类型=", StringComparison.Ordinal) >= 0 ||
                line.IndexOf("运行环境 | 版本=", StringComparison.Ordinal) >= 0)
            {
                ResetCourse();
                return;
            }

            Match name = CourseName.Match(line);
            if (name.Success)
            {
                courseTitle = (name.Groups[1].Success ? name.Groups[1].Value : name.Groups[2].Value).Trim();
                return;
            }

            Match catalog = CatalogCount.Match(line);
            if (catalog.Success)
            {
                int remaining = int.Parse(catalog.Groups[1].Value);
                int count = int.Parse(catalog.Groups[2].Value);
                if (remaining <= count)
                {
                    total = count;
                    doneAtStart = total - remaining;
                    completed.Clear();
                }
                return;
            }

            Match start = LessonStart.Match(line);
            if (start.Success)
            {
                lessonPosition = start.Groups[1].Value;
                lessonText = start.Groups[2].Value.Trim();
                int value = int.Parse(start.Groups[3].Value);
                progress = value <= 100 ? value : -1;
                playerSeconds = -1;
                durationSeconds = -1;
                return;
            }

            Match result = LessonResult.Match(line);
            if (result.Success)
            {
                string position = result.Groups[1].Value;
                string fields = " " + result.Groups[2].Value;
                int value = ReadPercent(PlatformProgress.Match(fields));
                if (value == 100 || (value < 0 && LegacyCompletion.IsMatch(fields)))
                    completed.Add(position);
                if (position == lessonPosition && value >= 0) progress = value;
                return;
            }

            if (lessonPosition.Length == 0) return;
            bool playback = line.IndexOf("播放状态 |", StringComparison.Ordinal) >= 0;
            if (!playback && line.IndexOf("课时完成 |", StringComparison.Ordinal) < 0 &&
                line.IndexOf("上报重试 |", StringComparison.Ordinal) < 0) return;

            int percent = ReadPercent(PlatformProgress.Match(line));
            if (percent >= 0) progress = percent;
            if (percent == 100) completed.Add(lessonPosition);

            Match time = playback ? PlayerTime.Match(line) : Match.Empty;
            if (time.Success)
            {
                playerSeconds = int.Parse(time.Groups[1].Value);
                durationSeconds = int.Parse(time.Groups[2].Value);
            }
        }
    }

    // ---------------- 游戏/全屏检测 ----------------
    internal static class Shell32
    {
        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);
    }

    internal static class GameGuard
    {
        public static bool IsFullScreen()
        {
            int q;
            int hr = Shell32.SHQueryUserNotificationState(out q);
            if (hr != 0) return false;
            return q == 3 || q == 7;
        }
    }

    // ---------------- 运行控制 ----------------
    internal class StudyRunner
    {
        private readonly string root;
        private readonly string runtimeDir;
        private readonly string configPath;
        private readonly string logsDir;
        private Process proc;
        private string tempConfig;

        private readonly LogTail logTail = new LogTail();
        private readonly CatalogCapture catalogCapture = new CatalogCapture();

        public StudyRunner(string root)
        {
            this.root = root;
            this.runtimeDir = ResolveRuntime(root);
            this.configPath = Path.Combine(root, "config.ini");
            this.logsDir = Path.Combine(this.runtimeDir, "logs");
        }

        // 上游发行包可放在根目录，也可放在 runtime 子目录
        private static string ResolveRuntime(string root)
        {
            string nested = Path.Combine(root, "runtime");
            if (File.Exists(Path.Combine(nested, "Autovisor.exe"))) return nested;
            return root;
        }

        public string RuntimeDir
        {
            get { return runtimeDir; }
        }

        public bool IsRunning
        {
            get
            {
                try
                {
                    if (proc != null && !proc.HasExited) return true;
                    Process[] all = Process.GetProcessesByName("Autovisor");
                    return all.Length > 0;
                }
                catch { return false; }
            }
        }

        public string Start(string password)
        {
            string exe = Path.Combine(runtimeDir, "Autovisor.exe");
            if (!File.Exists(exe)) return "未找到 Autovisor.exe，请确认 runtime 目录完整";

            CleanTempConfigs();
            tempConfig = Path.Combine(Path.GetTempPath(), "autovisor-panel-" + Guid.NewGuid().ToString("N") + ".ini");

            // 基于已保存的 config.ini 注入密码，生成一次性运行配置
            string baseText = File.ReadAllText(configPath, Encoding.UTF8);
            string injected = Regex.Replace(baseText, @"(?m)^password\s*=.*$", "password =" + (password ?? "").Replace("$", "$$"));
            File.WriteAllText(tempConfig, injected, new UTF8Encoding(false));

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = "--config \"" + tempConfig + "\"";
            psi.WorkingDirectory = runtimeDir;
            psi.UseShellExecute = true;
            catalogCapture.Prepare(psi, runtimeDir);

            try
            {
                DateTime startedUtc = DateTime.UtcNow;
                proc = Process.Start(psi);
                logTail.StartRun(startedUtc);
            }
            catch (Exception ex)
            {
                catalogCapture.Close();
                return "启动失败：" + ex.Message;
            }
            return null;
        }

        // 进程启动后调用：等程序读取完配置即删除临时文件
        public void PurgeTempConfig()
        {
            try
            {
                if (tempConfig != null && File.Exists(tempConfig)) File.Delete(tempConfig);
            }
            catch { }
            tempConfig = null;
        }

        // 只清理两小时前的残留，避免误删其他实例正在使用的运行配置
        public static void CleanTempConfigs()
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddHours(-2);
                foreach (string f in Directory.GetFiles(Path.GetTempPath(), "autovisor-panel-*.ini"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void Stop()
        {
            try
            {
                Process[] all = Process.GetProcessesByName("Autovisor");
                foreach (Process p in all)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("taskkill",
                            "/PID " + p.Id + " /T /F");
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = false;
                        Process.Start(psi).WaitForExit(4000);
                    }
                    catch { }
                }
            }
            catch { }
            PurgeTempConfig();
            catalogCapture.Close();
        }

        public void CloseCatalog()
        {
            catalogCapture.Close();
        }

        public StudySnapshot Snapshot()
        {
            StudySnapshot snapshot = logTail.Poll(logsDir, IsRunning);
            snapshot.Catalog = catalogCapture.Read();
            if (snapshot.Running && snapshot.Catalog != null && snapshot.Catalog.Lessons != null)
            {
                foreach (CatalogLesson lesson in snapshot.Catalog.Lessons)
                {
                    if (lesson == null || !lesson.Current) continue;
                    snapshot.LessonText = string.IsNullOrEmpty(lesson.Number)
                        ? lesson.Title : lesson.Number + " " + lesson.Title;
                    if (lesson.Progress >= 0 && lesson.Progress <= 100)
                        snapshot.Progress = lesson.Progress;
                    break;
                }
            }
            snapshot.Overview = LearningOverview.From(snapshot);
            return snapshot;
        }
    }
}
