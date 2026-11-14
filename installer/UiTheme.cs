using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArcaneThai
{
    internal static class Ui
    {
        public static readonly Color Canvas = ColorTranslator.FromHtml("#18262B");
        public static readonly Color Rail = ColorTranslator.FromHtml("#101A1F");
        public static readonly Color Surface = ColorTranslator.FromHtml("#203137");
        public static readonly Color Line = ColorTranslator.FromHtml("#3C5055");
        public static readonly Color Text = ColorTranslator.FromHtml("#ECF1EA");
        public static readonly Color Muted = ColorTranslator.FromHtml("#AFBEB9");
        public static readonly Color Gold = ColorTranslator.FromHtml("#D6B477");
        public static readonly Color Green = ColorTranslator.FromHtml("#9DCEAD");
        public static readonly Color Red = ColorTranslator.FromHtml("#F1ADA6");
        public static Font Font(float size = 10.5f, bool bold = false) { return new Font("Leelawadee UI", size, bold ? FontStyle.Bold : FontStyle.Regular); }
        public static Label Label(string text, float size = 10.5f, Color? color = null, bool bold = false)
        {
            return new Label { Text = text, Font = Font(size, bold), ForeColor = color ?? Text, BackColor = Color.Transparent, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft };
        }
        public static Panel Rule() { return new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Line, Margin = Padding.Empty }; }
    }

    internal sealed class ActionButton : Button
    {
        public bool Primary;
        public bool Navigation;
        private bool selected;
        private bool hovered;
        private bool pressed;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public ActionButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Font = Ui.Font(); Cursor = Cursors.Hand; Height = 42;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = !Enabled ? Color.FromArgb(42, 56, 60) : Primary ? (pressed ? Color.FromArgb(177, 141, 80) : hovered ? Color.FromArgb(232, 199, 140) : Ui.Gold) : selected || hovered ? Color.FromArgb(42, 62, 68) : Navigation ? Ui.Rail : Ui.Surface;
            Color foreground = !Enabled ? Color.FromArgb(137, 153, 150) : Primary ? Ui.Rail : selected ? Ui.Gold : Ui.Text;
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, ClientRectangle);
            if (!Navigation || selected) using (var pen = new Pen(selected ? Ui.Gold : Primary && Enabled ? Ui.Gold : Ui.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            var bounds = Navigation ? new Rectangle(14, 0, Width - 24, Height) : ClientRectangle;
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | (Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), foreground, background);
        }
    }

    internal sealed class ChoiceBox : CheckBox
    {
        public ChoiceBox() { AutoSize = false; Size = new Size(26, 26); Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            int inset = Math.Max(3, Width / 9);
            var box = new Rectangle(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
            using (var brush = new SolidBrush(Checked ? Ui.Gold : Ui.Rail)) e.Graphics.FillRectangle(brush, box);
            using (var pen = new Pen(Checked ? Ui.Gold : Ui.Muted)) e.Graphics.DrawRectangle(pen, box);
            if (Checked) using (var pen = new Pen(Ui.Rail, Math.Max(2.2f, Width / 12f))) e.Graphics.DrawLines(pen, new[] { new PointF(box.Left + box.Width * .2f, box.Top + box.Height * .5f), new PointF(box.Left + box.Width * .42f, box.Top + box.Height * .72f), new PointF(box.Left + box.Width * .84f, box.Top + box.Height * .28f) });
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Ui.Gold, BackColor);
        }
    }

    internal sealed class ComponentRow : TableLayoutPanel
    {
        public readonly ChoiceBox Choice = new ChoiceBox();
        public readonly Label State = Ui.Label("รอเลือกโฟลเดอร์", 9.5f, Ui.Muted);
        public ComponentRow(string title, string description)
        {
            ColumnCount = 3; RowCount = 2; Height = 82; Dock = DockStyle.Fill; BackColor = Ui.Surface; Margin = Padding.Empty; Padding = new Padding(16, 12, 16, 12);
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
            RowStyles.Add(new RowStyle(SizeType.Percent, 50)); RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            Choice.Checked = true; Choice.BackColor = Ui.Surface; Choice.Anchor = AnchorStyles.Left; Choice.AccessibleName = title;
            Controls.Add(Choice, 0, 0); SetRowSpan(Choice, 2);
            var heading = Ui.Label(title, 11.5f, Ui.Text, true);
            var caption = Ui.Label(description, 9.5f, Ui.Muted);
            Controls.Add(heading, 1, 0); Controls.Add(caption, 1, 1);
            State.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(State, 2, 0); SetRowSpan(State, 2);
            foreach (Control child in new Control[] { heading, caption, State }) { child.Cursor = Cursors.Hand; child.Click += (sender, e) => { if (Choice.Enabled) Choice.Checked = !Choice.Checked; }; }
        }
        public void SetState(string text, Color color) { State.Text = text; State.ForeColor = color; }
    }
}
