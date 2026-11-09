using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutovisorPanel
{
    internal static class T
    {
        public static readonly Color Bg      = Color.FromArgb(246, 247, 249);
        public static readonly Color Card    = Color.White;
        public static readonly Color Line    = Color.FromArgb(226, 229, 234);
        public static readonly Color Text    = Color.FromArgb(17, 24, 39);
        public static readonly Color Sub     = Color.FromArgb(107, 114, 128);
        public static readonly Color Accent  = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentH = Color.FromArgb(29, 78, 216);
        public static readonly Color Ok      = Color.FromArgb(22, 163, 74);
        public static readonly Color Warn    = Color.FromArgb(202, 138, 4);
        public static readonly Color Err     = Color.FromArgb(220, 38, 38);
        public static readonly Color Soft    = Color.FromArgb(240, 242, 245);

        public static Font F(float size)
        {
            return F(size, FontStyle.Regular);
        }

        public static Font F(float size, FontStyle style)
        {
            return new Font("微软雅黑", size, style, GraphicsUnit.Point);
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || d > r.Width || d > r.Height)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal class Card : Panel
    {
        public string Caption = "";
        public string SubCaption = "";

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = T.Bg;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : T.Bg);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = T.Round(r, 10))
            {
                using (SolidBrush b = new SolidBrush(T.Card)) g.FillPath(b, path);
                using (Pen pen = new Pen(T.Line)) g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Caption, T.F(11f, FontStyle.Bold),
                new Point(18, 15), T.Text, TextFormatFlags.NoPadding);
            if (SubCaption.Length > 0)
            {
                TextRenderer.DrawText(g, SubCaption, T.F(8.5f),
                    new Point(18, 40), T.Sub, TextFormatFlags.NoPadding);
            }
        }
    }

    internal class FlatButton : Button
    {
        public Color Fill = T.Accent;
        public Color HoverFill = T.AccentH;
        public Color TextColor = Color.White;
        public int Radius = 7;

        private bool hover;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = T.F(9.5f, FontStyle.Bold);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            Size = new Size(120, 38);
            DoubleBuffered = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : T.Card);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Enabled ? (hover ? HoverFill : Fill) : Color.FromArgb(209, 213, 219);
            using (GraphicsPath path = T.Round(r, Radius))
            using (SolidBrush b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
            }

            Color fg = Enabled ? TextColor : Color.White;
            TextRenderer.DrawText(g, Text, Font, r, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal class EyeButton : Control
    {
        public bool Hidden;
        private bool hover;

        public EyeButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            Size = new Size(24, 22);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);

            Color c = hover ? T.Text : T.Sub;
            int cx = Width / 2;
            int cy = Height / 2;
            int rx = 7;
            int ry = 4;

            using (Pen pen = new Pen(c, 1.4f))
            {
                g.DrawEllipse(pen, cx - rx, cy - ry, rx * 2, ry * 2);
            }
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillEllipse(b, cx - 2f, cy - 2f, 4f, 4f);
            }
            if (Hidden)
            {
                using (Pen pen = new Pen(c, 1.6f))
                {
                    g.DrawLine(pen, cx - 6, cy + 6, cx + 6, cy - 6);
                }
            }
        }
    }

    internal class InputBox : Panel
    {
        public readonly TextBox Box = new TextBox();
        private bool focused;
        private EyeButton eye;
        private int reserved;

        public InputBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Height = 30;

            Box.BorderStyle = BorderStyle.None;
            Box.Font = T.F(9.5f);
            Box.BackColor = Color.White;
            Box.ForeColor = T.Text;
            Box.Location = new Point(10, 6);
            Controls.Add(Box);

            Box.GotFocus += delegate { focused = true; Invalidate(); };
            Box.LostFocus += delegate { focused = false; Invalidate(); };
            LayoutBox();
        }

        public override string Text
        {
            get { return Box.Text; }
            set { Box.Text = value; }
        }

        public bool Revealed
        {
            get { return eye != null && Box.PasswordChar == '\0'; }
        }

        // 密码框：默认星号隐藏，右侧小眼睛切换明文
        public void EnableReveal()
        {
            if (eye != null) return;

            reserved = 26;
            Box.PasswordChar = '*';

            eye = new EyeButton();
            eye.Hidden = true;
            eye.Size = new Size(24, 22);
            eye.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            eye.Click += delegate
            {
                bool show = Box.PasswordChar != '\0';
                Box.PasswordChar = show ? '\0' : '*';
                eye.Hidden = !show;
                eye.Invalidate();
                Box.Focus();
                Box.SelectionStart = Box.TextLength;
            };
            Controls.Add(eye);
            LayoutBox();
        }

        private void LayoutBox()
        {
            Box.Width = Width - 20 - reserved;
            Box.Top = (Height - Box.Height) / 2;
            if (eye != null)
            {
                eye.Location = new Point(Width - 28, (Height - eye.Height) / 2);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutBox();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : T.Card);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = T.Round(r, 6))
            {
                using (SolidBrush b = new SolidBrush(Enabled ? Color.White : T.Soft)) g.FillPath(b, path);
                using (Pen pen = new Pen(focused ? T.Accent : T.Line)) g.DrawPath(pen, path);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Box.Focus();
            base.OnClick(e);
        }
    }

    internal class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            BackColor = T.Bg;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(T.Bg);
        }
    }

    internal class GripBar : Control
    {
        private bool hover;

        public GripBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = T.Bg;
            Cursor = Cursors.SizeWE;
            Width = 12;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(T.Bg);

            int cx = Width / 2;
            int cy = Height / 2;

            Color bar = hover ? Color.FromArgb(147, 197, 253) : Color.FromArgb(214, 220, 229);
            using (GraphicsPath p = T.Round(new Rectangle(cx - 2, 10, 4, Height - 20), 2))
            using (SolidBrush b = new SolidBrush(bar))
            {
                g.FillPath(b, p);
            }

            Color dot = hover ? T.Accent : Color.FromArgb(120, 133, 150);
            using (SolidBrush b = new SolidBrush(dot))
            {
                g.FillEllipse(b, cx - 2, cy - 24, 4, 4);
                g.FillEllipse(b, cx - 2, cy - 2, 4, 4);
                g.FillEllipse(b, cx - 2, cy + 20, 4, 4);
            }
        }
    }

    internal class Bar : Control
    {
        private int val;

        public Bar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 8;
            DoubleBuffered = true;
        }

        public int Value
        {
            get { return val; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                if (v != val) { val = v; Invalidate(); }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : T.Card);

            int radius = Height / 2;
            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = T.Round(track, radius))
            using (SolidBrush b = new SolidBrush(T.Soft))
            {
                g.FillPath(b, p);
            }

            int w = (int)((Width - 1) * val / 100.0);
            if (w > 0)
            {
                if (w < Height) w = Height;
                Rectangle fill = new Rectangle(0, 0, w, Height - 1);
                using (GraphicsPath p = T.Round(fill, radius))
                using (SolidBrush b = new SolidBrush(T.Accent))
                {
                    g.FillPath(b, p);
                }
            }
        }
    }

    internal class Pill : Control
    {
        public Color Fill = T.Soft;
        public Color TextColor = T.Sub;
        public int Radius = 12;

        public Pill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = T.F(9f, FontStyle.Bold);
            Size = new Size(96, 26);
            DoubleBuffered = true;
        }

        public void Set(string text, Color fill, Color fg)
        {
            Text = text;
            Fill = fill;
            TextColor = fg;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : T.Card);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = T.Round(r, Radius))
            using (SolidBrush b = new SolidBrush(Fill))
            {
                g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, Text, Font, r, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
