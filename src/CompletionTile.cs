using System.Drawing;
using System.Windows.Forms;

namespace AutovisorPanel
{
    internal sealed class CompletionTile : Panel
    {
        private readonly Bar bar;
        private readonly Label percent;
        private readonly Label done;
        private readonly Label remaining;
        private readonly string emptyText;

        public CompletionTile(string title, Color color, string emptyText)
        {
            this.emptyText = emptyText;
            BackColor = T.Bg;
            Size = new Size(T.Px(280), T.Px(86));
            Controls.Add(new Label {
                Text = title, AutoSize = true, Location = new Point(T.Px(11), T.Px(9)),
                ForeColor = T.Text, Font = T.F(10f, FontStyle.Bold)
            });
            percent = AddValue(color, T.F(12f, FontStyle.Bold), 75, 5, 63, true);
            bar = new Bar { FillColor = color };
            bar.SetBounds(T.Px(11), T.Px(37), Width - T.Px(22), T.Px(8));
            bar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            Controls.Add(bar);
            done = AddValue(T.Sub, T.F(9f), 11, 53, 120, false);
            remaining = AddValue(T.Sub, T.F(9f), 81, 53, 69, true);
            Resize += delegate
            {
                bool wide = Width >= T.Px(190);
                remaining.Visible = wide;
                done.Width = System.Math.Max(1, Width - T.Px(wide ? 94 : 22));
            };
            Render(default(CompletionCount));
        }

        private Label AddValue(Color color, Font font, int x, int y, int width, bool right)
        {
            Label value = new Label {
                ForeColor = color, Font = font,
                TextAlign = right ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft
            };
            value.SetBounds(right ? Width - T.Px(x) : T.Px(x), T.Px(y), T.Px(width), T.Px(27));
            value.Anchor = AnchorStyles.Top | (right ? AnchorStyles.Right : AnchorStyles.Left);
            Controls.Add(value);
            return value;
        }

        public void Render(CompletionCount count)
        {
            bar.Value = count.Percent;
            string percentText = count.Known ? count.Percent + "%" : "—";
            string doneText = count.Known ? count.Done + "/" + count.Total + " 节完成" : emptyText;
            string remainingText = count.Known ? "剩余 " + (count.Total - count.Done) + " 节" : "";
            if (percent.Text != percentText) percent.Text = percentText;
            if (done.Text != doneText) done.Text = doneText;
            if (remaining.Text != remainingText) remaining.Text = remainingText;
        }
    }
}
