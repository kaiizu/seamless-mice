using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace SeamlessMice
{
    // A compact numeric field with a themed spinner, used instead of NumericUpDown
    // because the native editor does not follow dark colors.
    internal sealed class NumericField : Control
    {
        private readonly TextBox edit = new TextBox { BorderStyle = BorderStyle.None };
        private decimal value, minimum, maximum = 100, increment = 1;
        private int decimals = 1;
        private bool updating, spinnerHover;

        internal NumericField()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            ForeColor = Theme.Ink;
            Height = 28;
            TabStop = false;
            edit.Font = Font;
            edit.BackColor = BackColor;
            edit.ForeColor = ForeColor;
            edit.TabStop = true;
            edit.TextAlign = HorizontalAlignment.Left;
            edit.TextChanged += delegate { if (!updating) Parse(); };
            edit.KeyDown += EditKeyDown;
            edit.Leave += delegate { Format(); Invalidate(); };
            edit.GotFocus += delegate { edit.SelectAll(); Invalidate(); };
            edit.LostFocus += delegate { Invalidate(); };
            Controls.Add(edit);
            Format();
        }

        internal event EventHandler ValueChanged;

        internal decimal Minimum
        {
            get { return minimum; }
            set { minimum = value; if (this.value < value) Value = value; }
        }

        internal decimal Maximum
        {
            get { return maximum; }
            set { maximum = value; if (this.value > value) Value = value; }
        }

        internal decimal Increment { get { return increment; } set { increment = Math.Max(0.1m, value); } }

        internal int DecimalPlaces
        {
            get { return decimals; }
            set { decimals = Math.Max(0, Math.Min(4, value)); Format(); }
        }

        internal decimal Value
        {
            get { return value; }
            set
            {
                decimal clamped = Math.Min(maximum, Math.Max(minimum, value));
                bool changed = clamped != this.value;
                this.value = clamped;
                Format();
                if (changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        private void Format()
        {
            updating = true;
            edit.Text = value.ToString("N" + decimals, CultureInfo.CurrentCulture);
            updating = false;
        }

        private void Parse()
        {
            decimal parsed;
            if (!Decimal.TryParse(edit.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed)) return;
            parsed = Math.Min(maximum, Math.Max(minimum, parsed));
            if (parsed == value) return;
            value = parsed;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        private void Step(int direction)
        {
            Value = value + direction * increment;
        }

        private void EditKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { Step(1); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter) { Format(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { Format(); e.Handled = true; e.SuppressKeyPress = true; }
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            if (edit == null) return;
            edit.BackColor = BackColor;
            Invalidate();
        }

        protected override void OnForeColorChanged(EventArgs e)
        {
            base.OnForeColorChanged(e);
            if (edit == null) return;
            edit.ForeColor = ForeColor;
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (edit == null) return;
            edit.Font = Font;
            Height = Math.Max(26, Font.Height + 10);
            LayoutEdit();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            edit.AccessibleName = AccessibleName;
            LayoutEdit();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutEdit();
            Theme.RoundRegion(this, 5);
        }

        private void LayoutEdit()
        {
            edit.SetBounds(7, Math.Max(0, (Height - edit.Height) / 2), Math.Max(10, Width - 31), edit.Height);
        }

        private bool InSpinner(Point point) { return point.X >= Width - 21; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (InSpinner(e.Location)) Step(e.Y < Height / 2 ? 1 : -1);
            else { edit.Focus(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = InSpinner(e.Location);
            if (hover != spinnerHover) { spinnerHover = hover; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!spinnerHover) return;
            spinnerHover = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (edit.Focused) Step(e.Delta > 0 ? 1 : -1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool focused = edit.Focused;
            using (var background = new SolidBrush(BackColor)) g.FillRectangle(background, ClientRectangle);
            if (spinnerHover)
                using (var hover = new SolidBrush(Theme.SurfaceHover)) g.FillRectangle(hover, Width - 21, 1, 20, Height - 2);
            using (var border = new Pen(focused ? Theme.Accent : Theme.BorderStrong))
                using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 5))
                    g.DrawPath(border, path);
            using (var separator = new Pen(Theme.Border)) g.DrawLine(separator, Width - 21, 2, Width - 21, Height - 3);
            using (var arrow = new SolidBrush(Theme.Muted))
            {
                int cx = Width - 11;
                g.FillPolygon(arrow, new[] { new Point(cx - 3, Height / 2 - 6), new Point(cx + 3, Height / 2 - 6), new Point(cx, Height / 2 - 3) });
                g.FillPolygon(arrow, new[] { new Point(cx - 3, Height / 2 + 6), new Point(cx + 3, Height / 2 + 6), new Point(cx, Height / 2 + 3) });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && edit != null) edit.Dispose();
            base.Dispose(disposing);
        }
    }
}
