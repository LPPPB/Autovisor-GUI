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
        public string LessonText = "—";
        public int Progress = -1;
        public int Done;
        public int Total;
        public string UpdatedText = "—";
        public double IdleMinutes = -1;
        public bool NeedAttention;
        public string AttentionText = "";
        public string Tail = "";
        public string LogFile = "";
    }

    // 增量读取日志：首轮读全量，之后只读新增部分，避免每 3 秒重复解析整个文件
    internal class LogTail
    {
        private static readonly Regex LessonStart = new Regex(@"课时开始 \| 序号=(\d+)/(\d+) 标题=(.+?) 平台进度=(\d+)%");
        private static readonly Regex PlayState = new Regex(@"播放状态 \| 已学=(\d+(?:\.\d+)?)min 平台进度=(\d+)%");
        private static readonly Regex DoneResult = new Regex(@"课时结果 \| 序号=\d+/\d+ .*?完成=True");

        private string filePath = "";
        private long offset;
        private int doneCount;
        private int total;
        private string lessonText = "—";
        private int progress = -1;
        private bool verifyPending;
        private readonly Queue<string> tail = new Queue<string>();

        public static string FindLatest(string logsDir)
        {
            if (!Directory.Exists(logsDir)) return null;
            string[] files = Directory.GetFiles(logsDir, "Log*.txt");
            if (files.Length == 0) return null;

            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string f in files)
            {
                DateTime t = File.GetLastWriteTime(f);
                if (t > bestTime) { bestTime = t; best = f; }
            }
            return best;
        }

        private void Reset()
        {
            offset = 0;
            doneCount = 0;
            total = 0;
            lessonText = "—";
            progress = -1;
            verifyPending = false;
            tail.Clear();
        }

        public StudySnapshot Poll(string logsDir, bool running)
        {
            StudySnapshot s = new StudySnapshot();
            s.Running = running;

            string latest = FindLatest(logsDir);
            if (latest == null) return s;
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
                return s;
            }

            s.LessonText = lessonText;
            s.Total = total;
            s.Done = doneCount;
            s.Progress = progress;

            DateTime wrote = File.GetLastWriteTime(filePath);
            s.UpdatedText = wrote.ToString("HH:mm:ss");
            s.IdleMinutes = Math.Round((DateTime.Now - wrote).TotalMinutes, 1);

            if (verifyPending)
            {
                s.NeedAttention = true;
                s.AttentionText = "检测到安全验证，需要人工处理";
            }
            else if (running && s.IdleMinutes > 10)
            {
                s.NeedAttention = true;
                s.AttentionText = "日志已静默 " + s.IdleMinutes + " 分钟，可能卡住";
            }

            s.Tail = string.Join(Environment.NewLine, tail.ToArray());
            return s;
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
                Consume(chunk.Replace("\r\n", "\n").Split('\n'));
            }
        }

        private void Consume(string[] lines)
        {
            foreach (string line in lines)
            {
                if (line.Length == 0) continue;

                Match m = LessonStart.Match(line);
                if (m.Success)
                {
                    total = int.Parse(m.Groups[2].Value);
                    lessonText = "第 " + m.Groups[1].Value + "/" + m.Groups[2].Value + " 节  " + m.Groups[3].Value.Trim();
                }

                m = PlayState.Match(line);
                if (m.Success)
                {
                    progress = int.Parse(m.Groups[2].Value);
                }

                if (line.IndexOf("课时完成 | 平台进度=100%", StringComparison.Ordinal) >= 0)
                {
                    progress = 100;
                }

                if (DoneResult.IsMatch(line)) doneCount++;

                if (line.IndexOf("检测到安全验证", StringComparison.Ordinal) >= 0) verifyPending = true;
                if (line.IndexOf("安全验证已完成", StringComparison.Ordinal) >= 0) verifyPending = false;

                tail.Enqueue(line);
                while (tail.Count > 40) tail.Dequeue();
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

            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
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
        }

        public StudySnapshot Snapshot()
        {
            return logTail.Poll(logsDir, IsRunning);
        }
    }
}
