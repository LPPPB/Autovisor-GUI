using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutovisorPanel
{
    internal partial class MainForm
    {
        private static readonly string[] PageTitles = { "学习概览", "课程目录", "课程设置", "运行日志" };
        private static readonly string[] PageDetails = {
            "当前章节、平台记录和整课完成情况",
            "按章浏览小节及其学习状态",
            "管理登录、浏览器和课程链接",
            "查看 Autovisor 最近的运行记录"
        };
        private readonly Control[] pages = new Control[4];
        private readonly Button[] navigation = new Button[4];
        private CatalogView catalogView;
        private Label pageTitle;
        private Label pageDetail;
        private int activePage = -1;

        private void BuildWorkspace()
        {
            Panel sidebar = new Panel { BackColor = Color.FromArgb(20, 30, 48) };
            Controls.Add(sidebar);
            Label brand = PlaceLabel(sidebar, "Autovisor", 18, 22, T.F(16f, FontStyle.Bold), Color.White);
            brand.AccessibleName = "Autovisor";
            PlaceLabel(sidebar, "学习工作台", 19, 56, T.F(10f), Color.FromArgb(167, 180, 200));
            for (int i = 0; i < PageTitles.Length; i++)
            {
                int index = i;
                Button item = new Button
                {
                    Text = PageTitles[i],
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.FromArgb(205, 216, 232),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(T.Px(12), 0, 0, 0),
                    Font = Font,
                    UseVisualStyleBackColor = false,
                    Cursor = Cursors.Hand
                };
                item.FlatAppearance.BorderSize = 0;
                item.FlatAppearance.MouseOverBackColor = Color.FromArgb(37, 52, 76);
                item.SetBounds(T.Px(10), T.Px(104 + 48 * i), T.Px(110), T.Px(40));
                item.Click += delegate { SelectPage(index); };
                navigation[i] = item;
                sidebar.Controls.Add(item);
            }

            Panel workspace = new Panel();
            Controls.Add(workspace);

            Panel header = new Panel { BackColor = T.Card };
            workspace.Controls.Add(header);
            pageTitle = PlaceLabel(header, "", 22, 16, T.F(17f, FontStyle.Bold), T.Text);
            pageDetail = PlaceLabel(header, "", 24, 55, Font, T.Sub);
            pageDetail.AutoSize = false;
            pageDetail.AutoEllipsis = true;

            pill = new Pill();
            pill.Size = new Size(T.Px(102), T.Px(29));
            pill.Set("未运行", T.Soft, T.Sub);
            header.Controls.Add(pill);
            btnStart = MakeAction(header, "开始学习", 0, 20, 100, true);
            btnStart.Click += delegate { StartStudy(); };
            btnStop = MakeAction(header, "停止", 0, 20, 62, false);
            btnStop.Click += delegate { StopStudy(); };
            attention = PlaceLabel(header, "", 0, 62, Font, T.Warn);
            attention.AutoSize = false;
            attention.AutoEllipsis = true;
            attention.TextAlign = ContentAlignment.MiddleRight;

            Panel host = new Panel();
            workspace.Controls.Add(host);
            pages[0] = BuildOverview();
            catalogView = new CatalogView();
            pages[1] = catalogView;
            pages[2] = BuildSettings();
            pages[3] = BuildLogs();
            foreach (Control page in pages)
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                host.Controls.Add(page);
            }
            SelectPage(0);

            EventHandler reflow = delegate
            {
                int sidebarWidth = T.Px(ClientSize.Width < T.Px(960) ? 136 : 180);
                int workspaceWidth = Math.Max(1, ClientSize.Width - sidebarWidth);
                bool narrowHeader = workspaceWidth < T.Px(630);
                int headerHeight = T.Px(narrowHeader ? 139 : 96);
                int actionTop = T.Px(narrowHeader ? 76 : 20);
                int inset = T.Px(narrowHeader ? 12 : 20);
                int stopX = workspaceWidth - inset - btnStop.Width;
                int startX = stopX - T.Px(10) - btnStart.Width;
                int pillX = startX - T.Px(10) - pill.Width;

                sidebar.SetBounds(0, 0, sidebarWidth, ClientSize.Height);
                foreach (Button item in navigation) item.Width = sidebarWidth - T.Px(20);
                workspace.SetBounds(sidebarWidth, 0, workspaceWidth, ClientSize.Height);
                workspace.SuspendLayout();
                header.SetBounds(0, 0, workspaceWidth, headerHeight);
                pill.Location = new Point(pillX, actionTop + T.Px(4));
                btnStart.Location = new Point(startX, actionTop);
                btnStop.Location = new Point(stopX, actionTop);
                pageDetail.Size = new Size(Math.Max(0, (narrowHeader ? workspaceWidth - inset : pillX - T.Px(12)) - pageDetail.Left), T.Px(23));
                int attentionX = narrowHeader ? inset : pillX;
                attention.SetBounds(attentionX, T.Px(narrowHeader ? 115 : 63),
                    Math.Max(0, workspaceWidth - inset - attentionX), T.Px(22));
                int hostWidth = Math.Max(1, Math.Min(T.Px(1240), workspaceWidth - 2 * inset));
                host.SetBounds((workspaceWidth - hostWidth) / 2, headerHeight + T.Px(12), hostWidth,
                    Math.Max(1, ClientSize.Height - headerHeight - T.Px(24)));
                workspace.ResumeLayout(true);
            };
            ClientSizeChanged += reflow;
            reflow(this, EventArgs.Empty);
        }

        private Control BuildOverview()
        {
            Panel overview = new Panel { Size = new Size(T.Px(730), T.Px(553)) };
            Card chapter = AddSection(overview, "当前学习", 0, 164);
            lblCourseTitle = PlaceLabel(chapter, "等待课程名称", 18, 43, T.F(15f, FontStyle.Bold), T.Text);
            lblCourseTitle.AutoSize = false;
            lblCourseTitle.AutoEllipsis = true;
            lblCourseTitle.SetBounds(T.Px(18), T.Px(43), chapter.Width - T.Px(36), T.Px(32));
            lblCourseTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblChapterTitle = PlaceLabel(chapter, "章节信息待同步", 18, 78, Font, T.Sub);
            lblChapterTitle.AutoSize = false;
            lblChapterTitle.AutoEllipsis = true;
            lblChapterTitle.SetBounds(T.Px(18), T.Px(78), chapter.Width - T.Px(36), T.Px(24));
            lblChapterTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            lessonNumber = new Pill();
            lessonNumber.SetBounds(T.Px(18), T.Px(114), T.Px(74), T.Px(27));
            lessonNumber.Set("小节", Color.FromArgb(235, 241, 255), T.Accent);
            chapter.Controls.Add(lessonNumber);
            lblLesson = PlaceLabel(chapter, "等待课程开始", 103, 111, T.F(12f, FontStyle.Bold), T.Text);
            lblLesson.AutoSize = false;
            lblLesson.AutoEllipsis = true;
            lblLesson.SetBounds(T.Px(103), T.Px(111), chapter.Width - T.Px(121), T.Px(44));
            lblLesson.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Card progress = AddSection(overview, "学习进度", 176, 248);
            PlaceLabel(progress, "当前小节 · 平台记录", 18, 51, Font, T.Sub);
            lblProgress = PlaceLabel(progress, "—", 0, 19, T.F(24f, FontStyle.Bold), T.Accent);
            lblProgress.AutoSize = false;
            lblProgress.SetBounds(progress.Width - T.Px(194), T.Px(19), T.Px(176), T.Px(53));
            lblProgress.TextAlign = ContentAlignment.MiddleRight;
            lblProgress.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            bar = new Bar();
            bar.SetBounds(T.Px(18), T.Px(80), progress.Width - T.Px(36), T.Px(12));
            bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progress.Controls.Add(bar);
            TableLayoutPanel completion = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, BackColor = T.Card };
            completion.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            completion.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            completion.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            completion.SetBounds(T.Px(18), T.Px(108), progress.Width - T.Px(36), T.Px(88));
            completion.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            courseCompletion = new CompletionTile("整课完成度", Color.FromArgb(79, 70, 229), "等待整课统计");
            chapterCompletion = new CompletionTile("本章完成度", Color.FromArgb(13, 133, 115), "等待课程目录");
            courseCompletion.Dock = chapterCompletion.Dock = DockStyle.Fill;
            courseCompletion.Margin = new Padding(0, 0, T.Px(6), 0);
            chapterCompletion.Margin = new Padding(T.Px(6), 0, 0, 0);
            completion.Controls.Add(courseCompletion, 0, 0);
            completion.Controls.Add(chapterCompletion, 1, 0);
            progress.Controls.Add(completion);
            lblPlayerTime = PlaceLabel(progress, "播放器 —", 18, 215, T.F(10f), T.Sub);
            lblVideoRemaining = PlaceLabel(progress, "视频剩余 —", 225, 215, T.F(10f), T.Sub);

            Card health = AddSection(overview, "运行监测", 436, 117);
            PlaceLabel(health, "最近日志", 18, 48, Font, T.Sub);
            lblUpdated = PlaceLabel(health, "—", 96, 48, Font, T.Text);
            Label idleTitle = PlaceLabel(health, "日志静默", 225, 48, Font, T.Sub);
            lblIdle = PlaceLabel(health, "未计时", 302, 48, Font, T.Sub);
            hintTip = PlaceLabel(health, "", 18, 79, Font, T.Sub);
            Panel scroll = MakeScrollable(overview);
            overview.SizeChanged += delegate
            {
                bool compact = overview.Width < T.Px(450);
                idleTitle.Location = new Point(T.Px(compact ? 18 : 225), T.Px(compact ? 75 : 48));
                lblIdle.Location = new Point(T.Px(compact ? 96 : 302), idleTitle.Top);
                hintTip.Top = T.Px(compact ? 106 : 79);
                health.Height = T.Px(compact ? 145 : 117);
                overview.Height = health.Bottom;
                scroll.AutoScrollMinSize = new Size(0, overview.Height);
            };
            return scroll;
        }

        private Control BuildSettings()
        {
            Card settings = new Card {
                Caption = "课程与播放设置",
                SubCaption = "保存后用于下一次启动",
                Size = new Size(T.Px(730), T.Px(515))
            };

            boxUser = new InputBox();
            boxPass = new InputBox();
            boxPass.EnableReveal();
            cbDriver = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Font };
            cbDriver.Items.AddRange(new object[] { "Edge", "Chrome" });
            boxExe = new InputBox();
            numMaxTime = new NumericUpDown { Minimum = 0, Maximum = 600, Font = Font };
            numSpeed = new NumericUpDown { Minimum = 1, Maximum = 1.8m, DecimalPlaces = 1, Increment = 0.1m, Font = Font };
            Panel[] fields = {
                AddField(settings, "账号", boxUser),
                AddField(settings, "密码", boxPass),
                AddField(settings, "浏览器", cbDriver),
                AddField(settings, "浏览器路径（可选）", boxExe),
                AddField(settings, "每门课时限（分钟，0 为不限）", numMaxTime),
                AddField(settings, "播放倍速", numSpeed)
            };

            FlowLayoutPanel toggles = new FlowLayoutPanel { BackColor = T.Card };
            settings.Controls.Add(toggles);
            chkSound = AddToggle(toggles, "静音播放");
            chkCaptcha = AddToggle(toggles, "自动过滑块");
            chkHide = AddToggle(toggles, "隐藏窗口");
            chkDonate = AddToggle(toggles, "显示赞赏码");
            chkGameGuard = AddToggle(toggles, "游戏时暂停");
            chkGameGuard.CheckedChanged += delegate
            {
                if (!chkGameGuard.Checked)
                {
                    gameActive = false;
                    pausedByGame = false;
                    hotCount = coldCount = 0;
                }
                UpdateGameHint();
                UpdateButtons();
            };

            Label links = PlaceLabel(settings, "课程链接", 18, 0, T.F(11f, FontStyle.Bold), T.Text);
            Label linksHint = PlaceLabel(settings, "每行一个 http 或 https 链接", 102, 0, Font, T.Sub);
            boxUrls = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Font = Font,
                BorderStyle = BorderStyle.FixedSingle
            };
            settings.Controls.Add(boxUrls);
            credPill = new Pill();
            credPill.Size = new Size(T.Px(91), T.Px(31));
            settings.Controls.Add(credPill);
            btnSave = MakeAction(settings, "保存设置", 0, 0, 105, true);
            btnSave.Click += delegate { SaveSettings(); };

            Panel scroll = MakeScrollable(settings);
            int previousWidth = -1;
            EventHandler reflow = delegate
            {
                if (settings.Width == previousWidth) return;
                previousWidth = settings.Width;
                settings.SuspendLayout();
                int inset = T.Px(18);
                int width = Math.Max(1, settings.Width - 2 * inset);
                int gap = T.Px(16);
                int columns = width < T.Px(635) ? 1 : 2;
                int fieldWidth = (width - (columns - 1) * gap) / columns;
                int rowHeight = T.Px(62);
                int fieldsTop = T.Px(67);
                for (int i = 0; i < fields.Length; i++)
                    fields[i].SetBounds(inset + i % columns * (fieldWidth + gap), fieldsTop + i / columns * rowHeight, fieldWidth, rowHeight);

                int togglesTop = fieldsTop + fields.Length / columns * rowHeight;
                int togglesHeight = T.Px(columns == 1 ? 70 : 42);
                toggles.SetBounds(inset, togglesTop, width, togglesHeight);
                int linksTop = togglesTop + togglesHeight + T.Px(8);
                links.Top = linksHint.Top = linksTop;
                boxUrls.SetBounds(inset, linksTop + T.Px(26), width, T.Px(101));
                btnSave.Location = new Point(settings.Width - inset - btnSave.Width, boxUrls.Bottom + T.Px(12));
                credPill.Location = new Point(inset, btnSave.Top + T.Px(4));
                settings.Height = btnSave.Bottom + T.Px(12);
                scroll.AutoScrollMinSize = new Size(0, settings.Height);
                settings.ResumeLayout();
            };
            settings.SizeChanged += reflow;
            reflow(settings, EventArgs.Empty);
            return scroll;
        }

        private Control BuildLogs()
        {
            Card logs = new Card { Caption = "最近日志", SubCaption = "显示上游最近 40 行", Size = new Size(T.Px(730), T.Px(560)) };
            boxLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(248, 250, 253),
                ForeColor = T.Text,
                Font = new Font("Consolas", T.Px(14), FontStyle.Regular, GraphicsUnit.Pixel)
            };
            boxLog.SetBounds(T.Px(18), T.Px(68), logs.Width - T.Px(36), logs.Height - T.Px(86));
            boxLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logs.Controls.Add(boxLog);
            return logs;
        }

        private void SelectPage(int index)
        {
            if (activePage == index) return;
            activePage = index;
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = i == index;
                navigation[i].BackColor = i == index ? Color.FromArgb(49, 69, 101) : Color.FromArgb(20, 30, 48);
                navigation[i].ForeColor = i == index ? Color.White : Color.FromArgb(205, 216, 232);
            }
            pageTitle.Text = PageTitles[index];
            pageDetail.Text = PageDetails[index];
            if (latestStatus == null) return;
            RenderActivePage(latestStatus);
            if (index == 1) catalogView.RevealCurrent();
        }

        private Label PlaceLabel(Control parent, string text, int x, int y, Font font, Color color)
        {
            Label label = new Label { AutoSize = true, Text = text, Font = font, ForeColor = color, Location = new Point(T.Px(x), T.Px(y)) };
            label.BackColor = Color.Transparent;
            parent.Controls.Add(label);
            return label;
        }

        private FlatButton MakeAction(Control parent, string text, int x, int y, int width, bool primary)
        {
            FlatButton button = new FlatButton { Text = text };
            if (!primary)
            {
                button.Fill = T.Soft;
                button.HoverFill = T.Line;
                button.TextColor = T.Text;
            }
            button.SetBounds(T.Px(x), T.Px(y), T.Px(width), T.Px(38));
            parent.Controls.Add(button);
            return button;
        }

        private static Card AddSection(Panel parent, string title, int y, int height)
        {
            Card section = new Card { Caption = title };
            section.SetBounds(0, T.Px(y), parent.Width, T.Px(height));
            section.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            parent.Controls.Add(section);
            return section;
        }

        private Panel AddField(Panel parent, string label, Control input)
        {
            Panel cell = new Panel { BackColor = T.Card };
            parent.Controls.Add(cell);
            PlaceLabel(cell, label, 0, 0, Font, T.Sub);
            input.SetBounds(0, T.Px(24), cell.Width, T.Px(32));
            input.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cell.Controls.Add(input);
            return cell;
        }

        private CheckBox AddToggle(FlowLayoutPanel parent, string text)
        {
            CheckBox toggle = new CheckBox { Text = text, AutoSize = true, Font = Font, Margin = new Padding(0, T.Px(3), T.Px(17), T.Px(7)) };
            parent.Controls.Add(toggle);
            return toggle;
        }

        private static Panel MakeScrollable(Panel surface)
        {
            Panel scroll = new Panel { AutoScroll = true };
            scroll.Controls.Add(surface);
            scroll.SizeChanged += delegate
            {
                // Reserving the scrollbar gutter keeps breakpoints stable when the bar appears.
                surface.Width = Math.Max(1, scroll.Width - Math.Max(SystemInformation.VerticalScrollBarWidth, T.Px(17)));
            };
            scroll.AutoScrollMinSize = new Size(0, surface.Height);
            return scroll;
        }
    }
}
