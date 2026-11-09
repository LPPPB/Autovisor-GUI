using System;
using System.Windows.Forms;

namespace AutovisorPanel
{
    internal partial class MainForm
    {
        private NotifyIcon tray;
        private ContextMenuStrip trayMenu;
        private DesktopNotifications notifications;

        private void BuildTray()
        {
            trayMenu = new ContextMenuStrip();
            ToolStripLabel status = new ToolStripLabel("状态：未运行");
            ToolStripLabel chapter = new ToolStripLabel { Visible = false };
            trayMenu.Items.Add(status);
            trayMenu.Items.Add(chapter);
            trayMenu.Items.Add(new ToolStripSeparator());
            AddTrayItem("打开面板", delegate { RestorePanel(); });
            AddTrayItem("查看学习概览", ShowTrayOverview);
            ToolStripMenuItem start = AddTrayItem("开始学习", StartStudy);
            ToolStripMenuItem stop = AddTrayItem("停止学习", StopStudy);
            trayMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem game = AddTrayItem("游戏时暂停", delegate { SaveTraySwitch(chkGameGuard, "panel", "gamePause"); });
            ToolStripMenuItem mute = AddTrayItem("静音播放（下次启动）", delegate { SaveTraySwitch(chkSound, "course-option", "soundOff"); });
            AddTrayItem("课程设置…", delegate { RestorePanel(); SelectPage(2); });
            trayMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = AddTrayItem("退出面板", delegate { RestorePanel(); Close(); });
            trayMenu.Opening += delegate
            {
                status.Text = "状态：" + TrayStatus();
                string title = latestStatus == null ? "" : latestStatus.LessonText;
                bool showChapter = latestStatus != null && latestStatus.Running && !string.IsNullOrEmpty(title) && title != "—";
                chapter.Available = showChapter;
                if (showChapter)
                {
                    string progress = latestStatus.Progress < 0 ? "" : " · " + latestStatus.Progress + "%";
                    chapter.Text = (title.Length > 28 ? title.Substring(0, 28) + "…" : title) + progress;
                    chapter.ToolTipText = title + progress;
                }
                start.Enabled = btnStart.Enabled;
                stop.Enabled = btnStop.Enabled;
                game.Checked = chkGameGuard.Checked;
                mute.Checked = chkSound.Checked;
                game.Enabled = mute.Enabled = exit.Enabled = !operationBusy && !closing;
            };

            tray = new NotifyIcon { Icon = Icon, Text = "Autovisor 面板", ContextMenuStrip = trayMenu };
            tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) RestorePanel();
            };
            tray.BalloonTipClicked += delegate { ShowTrayOverview(); };
            notifications = new DesktopNotifications(delegate
            {
                if (!IsDisposed && !Disposing && IsHandleCreated)
                    BeginInvoke((Action)ShowTrayOverview);
            });
            Disposed += delegate { DisposeTray(); };
        }

        private void StartTray()
        {
            tray.Visible = true;
            try { notifications.Start(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        private ToolStripMenuItem AddTrayItem(string text, Action action)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += delegate { action(); };
            trayMenu.Items.Add(item);
            return item;
        }

        private void ShowTrayOverview()
        {
            RestorePanel();
            SelectPage(0);
        }

        private string TrayStatus()
        {
            if (closing) return "正在退出";
            if (operationBusy) return "正在处理";
            if (gameActive) return "游戏暂停";
            if (latestStatus != null && latestStatus.NeedAttention) return "待处理";
            if (latestStatus != null && latestStatus.Running) return "学习中";
            return "未运行";
        }

        private void UpdateTrayTooltip()
        {
            if (tray == null) return;
            string value = "Autovisor 面板 · " + TrayStatus();
            if (tray.Text != value) tray.Text = value;
        }

        private async void SaveTraySwitch(CheckBox option, string section, string key)
        {
            if (operationBusy || closing) return;
            bool enabled = !option.Checked;
            string error = await RunOperation("正在保存设置…", delegate
            {
                Config.SaveSwitch(configPath, section, key, enabled);
                return null;
            });
            if (closing || IsDisposed) return;
            if (error == null) option.Checked = enabled;
            else
            {
                RestorePanel();
                ShowOperationError("保存失败", error);
            }
        }

        private void ShowToast(string title, string body)
        {
            if (tray == null) return;
            if (!notifications.Show(Icon, title, body))
            {
                try { tray.ShowBalloonTip(10000, title, body, ToolTipIcon.None); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }

        private void DisposeTray()
        {
            if (notifications != null)
            {
                notifications.Dispose();
                notifications = null;
            }
            if (tray == null) return;
            tray.Dispose();
            tray = null;
            trayMenu.Dispose();
        }
    }
}
