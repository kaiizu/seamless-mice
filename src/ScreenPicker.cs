using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SeamlessMice
{
    // Fully themed replacement for a DropDownList ComboBox. The native drop-down
    // button cannot follow dark colors, so this control draws both the field and list.
    internal sealed class ScreenPicker : Control
    {
        private readonly List<string> items = new List<string>();
        private int selectedIndex = -1;
        private bool hover;

        internal ScreenPicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.ComboBox;
            BackColor = Theme.Surface;
            ForeColor = Theme.Ink;
            Height = 28;
        }

        internal event EventHandler SelectedIndexChanged;

        internal int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int next = value >= 0 && value < items.Count ? value : -1;
                if (selectedIndex == next) return;
                selectedIndex = next;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        internal void SetItems(IEnumerable<string> values)
        {
            items.Clear();
            items.AddRange(values);
            selectedIndex = -1;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            return code == Keys.Up || code == Keys.Down || code == Keys.Space || code == Keys.Enter || base.IsInputKey(keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(hover ? Theme.SurfaceHover : Theme.Surface)) g.FillRectangle(background, ClientRectangle);
            using (var border = new Pen(Focused ? Theme.Accent : Theme.BorderStrong))
                using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 5))
                    g.DrawPath(border, path);
            string text = selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : "";
            TextRenderer.DrawText(g, text, Font, new Rectangle(8, 0, Math.Max(0, Width - 36), Height), Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var arrow = new Pen(Theme.Muted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                int cx = Width - 14, cy = Height / 2;
                g.DrawLines(arrow, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Theme.RoundRegion(this, 5);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) ShowDropDown();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || (e.KeyCode == Keys.Down && e.Alt))
            {
                ShowDropDown();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = Math.Max(0, selectedIndex - 1); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { SelectedIndex = Math.Min(items.Count - 1, selectedIndex + 1); e.Handled = true; e.SuppressKeyPress = true; }
        }

        private void ShowDropDown()
        {
            if (items.Count == 0) return;
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = true,
                BackColor = Theme.Surface,
                ForeColor = Theme.Ink,
                Font = Font,
                MinimumSize = new Size(Width, 0),
                Renderer = new MenuRenderer()
            };
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(items[i]) { Checked = i == selectedIndex, ForeColor = Theme.Ink };
                item.Click += delegate { SelectedIndex = index; };
                menu.Items.Add(item);
            }
            menu.Closed += delegate { menu.Dispose(); Focus(); };
            menu.Opened += delegate { Theme.RoundRegion(menu, 8); };
            menu.Show(this, new Point(0, Height));
        }

        private sealed class MenuRenderer : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(Theme.BorderStrong))
                using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 8))
                    e.Graphics.DrawPath(pen, path);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                using (var brush = new SolidBrush(e.Item.Selected ? Theme.AccentSoft : Theme.Surface))
                    e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = Theme.Ink;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                using (var pen = new Pen(Theme.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    int x = e.ImageRectangle.X + 4, y = e.ImageRectangle.Y + e.ImageRectangle.Height / 2;
                    e.Graphics.DrawLines(pen, new[] { new Point(x, y), new Point(x + 3, y + 3), new Point(x + 8, y - 4) });
                }
            }
        }
    }
}
