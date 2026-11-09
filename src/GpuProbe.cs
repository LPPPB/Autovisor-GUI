using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AutovisorPanel
{
    internal class GameHit
    {
        public int Pid;
        public string Name;
        public double Util;
    }

    // 通过 Windows 性能计数器 "GPU Engine" 读取每个进程的 3D 引擎占用，
    // 过滤掉系统/浏览器/开发工具后，占用超阈值的进程视为游戏。
    internal class GpuProbe
    {
        // 实例名形如 pid_1234_luid_0x00000000_0x0001133F_phys_0_eng_0_engtype_3D
        private static readonly Regex InstanceRx =
            new Regex(@"pid_(\d+)_.*engtype_([A-Za-z0-9_]+)\s*$", RegexOptions.IgnoreCase);

        private static readonly HashSet<string> Ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 系统与桌面合成
            "system", "registry", "memory compression", "idle", "svchost", "csrss", "wininit",
            "winlogon", "fontdrvhost", "dwm", "explorer", "searchhost", "shellexperiencehost",
            "startmenuexperiencehost", "applicationframehost", "textinputhost", "sihost",
            "ctfmon", "lockapp", "taskmgr", "dllhost", "runtimebroker", "audiodg", "smss",
            "services", "lsass", "wmiprvse",
            // 浏览器（含本工具自启的 Playwright Edge）
            "msedge", "chrome", "firefox", "opera", "brave", "vivaldi", "360se", "qqbrowser",
            // 开发工具与终端
            "powershell", "pwsh", "cmd", "conhost", "windowsterminal", "node", "python",
            "pythonw", "code", "devenv", "idea64", "clion64", "pycharm64", "webstorm64",
            "sublime_text", "notepad++", "trae cn", "trae", "cursor", "windsurf",
            "msedgewebview2", "webviewhost",
            // 播放器与通讯
            "obs64", "vlc", "mpc-hc64", "wmplayer", "spotify", "discord", "slack", "teams",
            "ms-teams", "steam", "steamwebhelper",
            // 本工具自身
            "autovisorpanel", "autovisor"
        };

        private readonly Dictionary<string, PerformanceCounter> cache =
            new Dictionary<string, PerformanceCounter>();
        private int failCount;
        private int skipTick;

        private const int FailLimit = 3;

        // 连续失败 3 次才判定不可用，之后每 6 个采样周期重试一次，成功即自动恢复
        public bool Available
        {
            get { return failCount < FailLimit; }
        }

        public GameHit Sample(double minPercent)
        {
            if (failCount >= FailLimit && (skipTick++ % 6) != 0) return null;

            try
            {
                PerformanceCounterCategory cat = new PerformanceCounterCategory("GPU Engine");
                string[] instances = cat.GetInstanceNames();

                Dictionary<int, double> perPid = new Dictionary<int, double>();
                List<string> alive = new List<string>();

                foreach (string inst in instances)
                {
                    Match m = InstanceRx.Match(inst);
                    if (!m.Success) continue;
                    if (!m.Groups[2].Value.Equals("3D", StringComparison.OrdinalIgnoreCase)) continue;

                    alive.Add(inst);
                    int pid = int.Parse(m.Groups[1].Value);

                    PerformanceCounter pc;
                    if (!cache.TryGetValue(inst, out pc))
                    {
                        // 实例可能在枚举后瞬间消失，单独兜住，不影响整体可用性
                        try
                        {
                            pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                        }
                        catch
                        {
                            continue;
                        }
                        cache[inst] = pc;
                        try { pc.NextValue(); } catch { }
                        continue;
                    }

                    float v;
                    try { v = pc.NextValue(); }
                    catch { continue; }

                    if (v <= 0) continue;

                    double cur = perPid.ContainsKey(pid) ? perPid[pid] : 0.0;
                    perPid[pid] = cur + v;
                }

                Prune(alive);

                GameHit best = null;
                foreach (KeyValuePair<int, double> kv in perPid)
                {
                    if (kv.Value < minPercent) continue;
                    string name = NameOf(kv.Key);
                    if (name.Length == 0 || Ignore.Contains(name)) continue;
                    if (best == null || kv.Value > best.Util)
                    {
                        best = new GameHit();
                        best.Pid = kv.Key;
                        best.Name = name;
                        best.Util = kv.Value;
                    }
                }

                failCount = 0;
                return best;
            }
            catch
            {
                if (failCount < FailLimit) failCount++;
                return null;
            }
        }

        private void Prune(List<string> alive)
        {
            if (cache.Count <= alive.Count) return;
            HashSet<string> keep = new HashSet<string>(alive);
            List<string> dead = new List<string>();
            foreach (string key in cache.Keys)
            {
                if (!keep.Contains(key)) dead.Add(key);
            }
            foreach (string key in dead)
            {
                try { cache[key].Dispose(); } catch { }
                cache.Remove(key);
            }
        }

        private static string NameOf(int pid)
        {
            try
            {
                return Process.GetProcessById(pid).ProcessName.ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }
    }
}
