using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutovisorPanel
{
    internal sealed class CatalogView : Card
    {
        private readonly TreeView tree;
        private readonly Label empty;
        private readonly FlatButton expandAll;
        private readonly FlatButton collapseAll;
        private readonly Font chapterFont = T.F(11f, FontStyle.Bold);
        private readonly Font detailFont = T.F(10f);
        private readonly List<TreeNode> lessonNodes = new List<TreeNode>();
        private CourseCatalog catalog;
        private TreeNode current;
        private TreeNode followedChapter;
        private bool? bulkExpansion;

        protected override int CaptionRightInset { get { return 212; } }

        private sealed class Chapter
        {
            public string Title;
            public int Done;
            public int Total;

            public bool Complete { get { return Done == Total && Total > 0; } }
            public string Progress { get { return Done + "/" + Total + " 节 · " + (Done * 100 / Total) + "%"; } }
        }

        public CatalogView()
        {
            Caption = "课程目录";
            Size = new Size(T.Px(438), T.Px(600));
            expandAll = AddCatalogAction("全部展开", 194, true);
            collapseAll = AddCatalogAction("全部收起", 102, false);

            tree = new TreeView();
            tree.SetBounds(T.Px(18), T.Px(68), Width - T.Px(36), Height - T.Px(86));
            tree.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            tree.BorderStyle = BorderStyle.None;
            tree.Font = T.F(11f);
            tree.ItemHeight = T.Px(41);
            tree.Indent = T.Px(18);
            tree.FullRowSelect = true;
            tree.ShowLines = false;
            tree.ShowPlusMinus = false;
            tree.ShowNodeToolTips = true;
            tree.DrawMode = TreeViewDrawMode.OwnerDrawAll;
            tree.DrawNode += DrawRow;
            tree.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Node.Parent == null) ToggleChapter(e.Node);
            };
            tree.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Space || tree.SelectedNode == null || tree.SelectedNode.Parent != null) return;
                ToggleChapter(tree.SelectedNode);
                e.SuppressKeyPress = true;
            };
            Controls.Add(tree);

            empty = new Label();
            empty.SetBounds(T.Px(18), T.Px(68), Width - T.Px(36), T.Px(92));
            empty.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            empty.BackColor = T.Card;
            empty.ForeColor = T.Sub;
            empty.Font = T.F(11f);
            Controls.Add(empty);
            Render(null);
        }

        public void RevealCurrent()
        {
            if (current != null && current.Parent.IsExpanded) current.EnsureVisible();
        }

        private FlatButton AddCatalogAction(string text, int right, bool expanded)
        {
            FlatButton button = new FlatButton
            {
                Text = text, Fill = T.Soft, HoverFill = T.Line, TextColor = T.Text
            };
            button.SetBounds(Width - T.Px(right), T.Px(12), T.Px(84), T.Px(32));
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button.Click += delegate
            {
                bulkExpansion = expanded;
                tree.BeginUpdate();
                try { ApplyBulkExpansion(expanded); }
                finally { tree.EndUpdate(); }
            };
            Controls.Add(button);
            return button;
        }

        private void ToggleChapter(TreeNode chapter)
        {
            bulkExpansion = null;
            chapter.Toggle();
        }

        private void ApplyBulkExpansion(bool expanded)
        {
            if (expanded) tree.ExpandAll();
            else tree.CollapseAll();
        }

        public void Render(CourseCatalog next)
        {
            if (next == null || next.Lessons == null || next.Lessons.Length == 0)
            {
                if (catalog != null)
                {
                    tree.Nodes.Clear();
                    lessonNodes.Clear();
                    current = null;
                    followedChapter = null;
                    catalog = null;
                }
                bulkExpansion = null;
                expandAll.Enabled = collapseAll.Enabled = false;
                Caption = "等待课程网页";
                SubCaption = "";
                empty.Text = "从本面板开始学习后，打开课程页即可读取目录。若上游驱动或网页目录不受支持，进度仍可在「运行状态」查看。";
                tree.Visible = false;
                empty.Visible = true;
                Invalidate();
                return;
            }
            if (ReferenceEquals(next, catalog)) return;

            bool rebuild = !SameLessons(catalog, next);
            if (catalog != null && !string.IsNullOrEmpty(catalog.Title) && !string.IsNullOrEmpty(next.Title) && catalog.Title != next.Title)
            {
                bulkExpansion = null;
                rebuild = true;
            }
            TreeNode previousCurrent = current;
            current = null;
            tree.BeginUpdate();
            try
            {
                if (rebuild)
                {
                    tree.Nodes.Clear();
                    lessonNodes.Clear();
                    followedChapter = null;
                    Dictionary<string, TreeNode> chapters = new Dictionary<string, TreeNode>();
                    foreach (CatalogLesson lesson in next.Lessons)
                    {
                        string key = string.IsNullOrEmpty(lesson.Chapter) ? "课程小节" : lesson.Chapter;
                        TreeNode group;
                        if (!chapters.TryGetValue(key, out group))
                        {
                            group = tree.Nodes.Add(key);
                            group.Tag = new Chapter { Title = key };
                            chapters.Add(key, group);
                        }
                        lessonNodes.Add(group.Nodes.Add(""));
                    }
                }

                foreach (TreeNode group in tree.Nodes)
                {
                    Chapter chapter = (Chapter)group.Tag;
                    chapter.Done = 0;
                    chapter.Total = group.Nodes.Count;
                }

                for (int i = 0; i < next.Lessons.Length; i++)
                {
                    CatalogLesson lesson = next.Lessons[i];
                    TreeNode node = lessonNodes[i];
                    string title = string.IsNullOrEmpty(lesson.Number) ? lesson.Title : lesson.Number + "  " + lesson.Title;
                    string state = lesson.Progress >= 100 ? "✓" : (lesson.Current ? "▶" : "○");
                    string percent = lesson.Progress >= 0 && lesson.Progress < 100 ? "  ·  " + lesson.Progress + "%" : "";
                    string text = state + "  " + title + percent;
                    if (node.Text != text) node.Text = text;
                    string tooltip = title + percent;
                    if (node.ToolTipText != tooltip) node.ToolTipText = tooltip;
                    node.Tag = lesson;
                    Chapter chapter = (Chapter)node.Parent.Tag;
                    if (lesson.Progress >= 100) chapter.Done++;
                    if (lesson.Current) current = node;
                }

                foreach (TreeNode group in tree.Nodes)
                {
                    Chapter chapter = (Chapter)group.Tag;
                    string text = chapter.Title + "  " + (chapter.Complete ? "✓ " : "") + chapter.Progress;
                    if (group.Text != text)
                    {
                        group.Text = text;
                        group.ToolTipText = text;
                    }
                }

                if (rebuild && bulkExpansion.HasValue) ApplyBulkExpansion(bulkExpansion.Value);
                if (current != null && current.Parent != followedChapter)
                {
                    if (!bulkExpansion.HasValue)
                    {
                        if (followedChapter != null) followedChapter.Collapse();
                        current.Parent.Expand();
                    }
                    followedChapter = current.Parent;
                }
            }
            finally { tree.EndUpdate(); }

            catalog = next;
            Caption = string.IsNullOrEmpty(next.Title) ? "当前课程" : next.Title;
            SubCaption = "已读取 " + next.Lessons.Length + " 节";
            empty.Visible = false;
            tree.Visible = true;
            expandAll.Enabled = collapseAll.Enabled = true;
            if (current != null && current != previousCurrent && current.Parent.IsExpanded && Visible) RevealCurrent();
            Invalidate();
            tree.Invalidate();
        }

        private static bool SameLessons(CourseCatalog previous, CourseCatalog next)
        {
            if (previous == null || previous.Lessons.Length != next.Lessons.Length) return false;
            for (int i = 0; i < next.Lessons.Length; i++)
            {
                CatalogLesson before = previous.Lessons[i];
                CatalogLesson after = next.Lessons[i];
                if (before.Chapter != after.Chapter || before.Number != after.Number || before.Title != after.Title)
                    return false;
            }
            return true;
        }

        private void DrawRow(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Bounds.Height <= 0) return;
            Rectangle row = new Rectangle(0, e.Bounds.Top, tree.ClientSize.Width, e.Bounds.Height);
            Chapter chapter = e.Node.Tag as Chapter;
            bool active = e.Node == current || (current != null && e.Node == current.Parent);
            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            Color fill = active ? Color.FromArgb(235, 241, 255) : selected ? T.Soft : chapter != null ? T.Bg : T.Card;
            using (SolidBrush brush = new SolidBrush(fill))
                e.Graphics.FillRectangle(brush, 0, row.Top + T.Px(2), row.Width, row.Height - T.Px(4));

            if (chapter != null)
            {
                int center = row.Top + row.Height / 2;
                using (Pen pen = new Pen(active ? T.Accent : T.Sub, 1.5f * T.ScaleFactor))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Point[] chevron = e.Node.IsExpanded
                        ? new[] { new Point(T.Px(12), center - T.Px(2)), new Point(T.Px(16), center + T.Px(2)), new Point(T.Px(20), center - T.Px(2)) }
                        : new[] { new Point(T.Px(14), center - T.Px(4)), new Point(T.Px(18), center), new Point(T.Px(14), center + T.Px(4)) };
                    e.Graphics.DrawLines(pen, chevron);
                }
                string progress = chapter.Complete ? "✓ " + chapter.Progress : chapter.Progress;
                int progressWidth = TextRenderer.MeasureText(e.Graphics, progress, detailFont, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + T.Px(4);
                int progressLeft = row.Width - T.Px(12) - progressWidth;
                DrawText(e.Graphics, chapter.Title, chapterFont, row, T.Px(30), progressLeft - T.Px(40),
                    active ? T.Accent : T.Text, false);
                DrawText(e.Graphics, progress, detailFont, row, progressLeft, progressWidth,
                    chapter.Complete ? T.Ok : active ? T.Accent : T.Sub, true);
                return;
            }

            CatalogLesson lesson = e.Node.Tag as CatalogLesson;
            if (lesson == null) return;
            bool done = lesson.Progress >= 100;
            string mark = done ? "✓" : active ? "▶" : "○";
            Color markColor = done ? T.Ok : active ? T.Accent : T.Sub;
            DrawText(e.Graphics, mark, tree.Font, row, T.Px(30), T.Px(23), markColor, false);
            string title = string.IsNullOrEmpty(lesson.Number) ? lesson.Title : lesson.Number + "  " + lesson.Title;
            bool showPercent = lesson.Progress >= 0 && lesson.Progress < 100;
            DrawText(e.Graphics, title, tree.Font, row, T.Px(55), row.Width - T.Px(showPercent ? 117 : 66),
                active ? T.Accent : T.Text, false);
            if (showPercent)
                DrawText(e.Graphics, lesson.Progress + "%", detailFont, row, row.Width - T.Px(58), T.Px(46),
                    active ? T.Accent : T.Sub, true);
        }

        private static void DrawText(Graphics graphics, string text, Font font, Rectangle row, int x, int width,
            Color color, bool alignRight)
        {
            if (width <= 0) return;
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (alignRight) flags |= TextFormatFlags.Right;
            TextRenderer.DrawText(graphics, text, font, new Rectangle(x, row.Top, width, row.Height), color, flags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                chapterFont.Dispose();
                detailFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
