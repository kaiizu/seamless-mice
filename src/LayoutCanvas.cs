using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace SeamlessMice
{
    internal sealed class LayoutCanvas : Control
    {
        internal Display[] Displays;
        internal Configuration Config;
        internal int Selected;
        internal event Action<int> SelectionChanged;
        internal event Action LayoutChanged;
        private RectangleF[] hitboxes = new RectangleF[0];
        private bool dragging, fit = true;
        private int hovered = -1;
        private Point dragOrigin;
        private double originalLeft, originalTop, scale = 1, offsetX, offsetY;

        private readonly Font indexFont = new Font("Segoe UI Semibold", 12f);
        private readonly Font nameFont = new Font("Segoe UI Semibold", 10f);
        private readonly Font sizeFont = new Font("Segoe UI", 8.5f);
        private readonly Font emptyFont = new Font("Segoe UI", 10f);
        private readonly SolidBrush gridBrush = new SolidBrush(Theme.CanvasGrid);
        private readonly SolidBrush selectedFill = new SolidBrush(Theme.AccentSoft);
        private readonly SolidBrush plainFill = new SolidBrush(Theme.PanelFill);
        private readonly SolidBrush badgeBrush = new SolidBrush(Theme.Badge);
        private readonly SolidBrush inkBrush = new SolidBrush(Theme.Ink);
        private readonly SolidBrush mutedBrush = new SolidBrush(Theme.Muted);
        private readonly SolidBrush accentBrush = new SolidBrush(Theme.Accent);
        private readonly SolidBrush whiteBrush = new SolidBrush(Color.White);
        private readonly Pen selectedPen = new Pen(Theme.Accent, 2);
        private readonly Pen hoverPen = new Pen(Theme.BorderStrong);
        private readonly Pen edgePen = new Pen(Theme.PanelEdge);
        private readonly Pen framePen = new Pen(Theme.Border);
        private readonly StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

        internal LayoutCanvas()
        {
            DoubleBuffered = true;
            TabStop = true;
            BackColor = Theme.Surface;
            AccessibleName = "Physical display layout. Drag a screen or use arrow keys.";
            SetStyle(ControlStyles.Selectable, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        internal void Fit() { fit = true; Invalidate(); }

        internal void ApplyPalette()
        {
            BackColor = Theme.Surface;
            gridBrush.Color = Theme.CanvasGrid;
            selectedFill.Color = Theme.AccentSoft;
            plainFill.Color = Theme.PanelFill;
            badgeBrush.Color = Theme.Badge;
            inkBrush.Color = Theme.Ink;
            mutedBrush.Color = Theme.Muted;
            accentBrush.Color = Theme.Accent;
            selectedPen.Color = Theme.Accent;
            hoverPen.Color = Theme.BorderStrong;
            edgePen.Color = Theme.PanelEdge;
            framePen.Color = Theme.Border;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Fit();
            Theme.RoundRegion(this, 10);
        }

        protected override bool IsInputKey(Keys key)
        {
            Keys code = key & Keys.KeyCode;
            return code == Keys.Left || code == Keys.Right || code == Keys.Up || code == Keys.Down || base.IsInputKey(key);
        }

        internal void Nudge(int x, int y, bool fast)
        {
            if (Config == null || Displays == null || Selected < 0 || Selected >= Displays.Length) return;
            Calibration c = Config.Screens[Displays[Selected].Id];
            double step = fast ? 10 : 1;
            c.LeftMm = Math.Max(-10000, Math.Min(10000, c.LeftMm + x * step));
            c.TopMm = Math.Max(-10000, Math.Min(10000, c.TopMm + y * step));
            Changed();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && dragging)
            {
                Calibration c = Config.Screens[Displays[Selected].Id];
                c.LeftMm = originalLeft;
                c.TopMm = originalTop;
                dragging = false;
                Capture = false;
                Cursor = Cursors.Default;
                Changed();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                Nudge(e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0,
                    e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Down ? 1 : 0, e.Shift);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private int HitTest(Point point)
        {
            for (int i = hitboxes.Length - 1; i >= 0; i--) if (hitboxes[i].Contains(point)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            int hit = HitTest(e.Location);
            if (hit < 0) return;
            Selected = hit;
            if (SelectionChanged != null) SelectionChanged(hit);
            Calibration c = Config.Screens[Displays[hit].Id];
            originalLeft = c.LeftMm;
            originalTop = c.TopMm;
            dragOrigin = e.Location;
            dragging = true;
            hovered = hit;
            Capture = true;
            Cursor = Cursors.SizeAll;
            Invalidate();
        }

        internal void DragTo(double desiredLeft, double desiredTop, bool snap)
        {
            if (Config == null || Displays == null || Selected < 0 || Selected >= Displays.Length) return;
            Calibration c = Config.Screens[Displays[Selected].Id];
            double x = desiredLeft, y = desiredTop;
            double tolerance = 7 / Math.Max(0.01, scale), bestX = tolerance, bestY = tolerance;
            if (snap)
                for (int i = 0; i < Displays.Length; i++)
                {
                    if (i == Selected) continue;
                    Calibration q = Config.Screens[Displays[i].Id];
                    double[] xs = { q.LeftMm, q.LeftMm + q.WidthMm, q.LeftMm - c.WidthMm, q.LeftMm + q.WidthMm - c.WidthMm, q.LeftMm + (q.WidthMm - c.WidthMm) / 2 };
                    double[] ys = { q.TopMm, q.TopMm + q.HeightMm, q.TopMm - c.HeightMm, q.TopMm + q.HeightMm - c.HeightMm, q.TopMm + (q.HeightMm - c.HeightMm) / 2 };
                    foreach (double candidate in xs) if (Math.Abs(candidate - desiredLeft) < bestX) { bestX = Math.Abs(candidate - desiredLeft); x = candidate; }
                    foreach (double candidate in ys) if (Math.Abs(candidate - desiredTop) < bestY) { bestY = Math.Abs(candidate - desiredTop); y = candidate; }
                }
            c.LeftMm = Math.Round(Math.Max(-10000, Math.Min(10000, x)), 1);
            c.TopMm = Math.Round(Math.Max(-10000, Math.Min(10000, y)), 1);
            Changed();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                DragTo(originalLeft + (e.X - dragOrigin.X) / scale, originalTop + (e.Y - dragOrigin.Y) / scale, (ModifierKeys & Keys.Alt) == 0);
                return;
            }
            int hit = HitTest(e.Location);
            if (hit == hovered) return;
            hovered = hit;
            Cursor = hit >= 0 ? Cursors.SizeAll : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragging || hovered < 0) return;
            hovered = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = false;
            Capture = false;
            hovered = HitTest(e.Location);
            Cursor = hovered >= 0 ? Cursors.SizeAll : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && dragging)
            {
                dragging = false;
                hovered = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        private void Changed()
        {
            if (LayoutChanged != null) LayoutChanged();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int y = 20; y < Height; y += 24)
                for (int x = 20; x < Width; x += 24)
                    g.FillRectangle(gridBrush, x, y, 1, 1);
            using (var frame = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10))
                g.DrawPath(framePen, frame);
            if (Displays == null || Displays.Length == 0 || Config == null)
            {
                g.DrawString("No screens detected", emptyFont, mutedBrush, ClientRectangle, centered);
                return;
            }
            if (fit && !dragging)
            {
                double minX = Double.MaxValue, minY = Double.MaxValue, maxX = Double.MinValue, maxY = Double.MinValue;
                foreach (Display d in Displays)
                {
                    Calibration c = Config.Screens[d.Id];
                    minX = Math.Min(minX, c.LeftMm);
                    minY = Math.Min(minY, c.TopMm);
                    maxX = Math.Max(maxX, c.LeftMm + c.WidthMm);
                    maxY = Math.Max(maxY, c.TopMm + c.HeightMm);
                }
                scale = Math.Max(0.01, Math.Min((Width - 80.0) / Math.Max(1, maxX - minX), (Height - 64.0) / Math.Max(1, maxY - minY)));
                offsetX = (Width - (maxX - minX) * scale) / 2 - minX * scale;
                offsetY = (Height - (maxY - minY) * scale) / 2 - minY * scale;
                fit = false;
            }
            hitboxes = new RectangleF[Displays.Length];
            for (int i = 0; i < Displays.Length; i++)
            {
                Calibration c = Config.Screens[Displays[i].Id];
                var rect = new RectangleF((float)(offsetX + c.LeftMm * scale), (float)(offsetY + c.TopMm * scale), (float)(c.WidthMm * scale), (float)(c.HeightMm * scale));
                hitboxes[i] = rect;
                bool isSelected = i == Selected;
                g.FillRectangle(isSelected ? selectedFill : plainFill, rect);
                Pen border = isSelected ? selectedPen : i == hovered ? hoverPen : edgePen;
                float inset = isSelected ? 1f : 0.5f;
                g.DrawRectangle(border, rect.X + inset, rect.Y + inset, Math.Max(0, rect.Width - inset * 2), Math.Max(0, rect.Height - inset * 2));
                if (rect.Width > 90 && rect.Height > 74)
                {
                    var badge = new RectangleF(rect.X + 8, rect.Y + 8, 24, 24);
                    g.FillEllipse(isSelected ? accentBrush : badgeBrush, badge);
                    g.DrawString((i + 1).ToString(CultureInfo.InvariantCulture), indexFont, isSelected ? whiteBrush : inkBrush, badge, centered);
                    g.DrawString(Displays[i].Name, nameFont, inkBrush, new RectangleF(rect.X + 8, rect.Y + rect.Height / 2 - 18, rect.Width - 16, 24), centered);
                    string size = (Math.Sqrt(c.WidthMm * c.WidthMm + c.HeightMm * c.HeightMm) / 25.4).ToString("0.#", CultureInfo.CurrentCulture) + " in";
                    g.DrawString(size, sizeFont, mutedBrush, new RectangleF(rect.X + 8, rect.Y + rect.Height - 28, rect.Width - 16, 18), centered);
                }
                else if (rect.Width > 46 && rect.Height > 46)
                {
                    g.DrawString((i + 1).ToString(CultureInfo.InvariantCulture), indexFont, inkBrush, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), centered);
                }
            }
            if (Focused)
                using (var focusPath = Theme.RoundRect(new RectangleF(3.5f, 3.5f, Width - 7, Height - 7), 7))
                using (var focus = new Pen(Theme.Accent, 1f))
                    g.DrawPath(focus, focusPath);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                indexFont.Dispose();
                nameFont.Dispose();
                sizeFont.Dispose();
                emptyFont.Dispose();
                gridBrush.Dispose();
                selectedFill.Dispose();
                plainFill.Dispose();
                badgeBrush.Dispose();
                inkBrush.Dispose();
                mutedBrush.Dispose();
                accentBrush.Dispose();
                whiteBrush.Dispose();
                selectedPen.Dispose();
                hoverPen.Dispose();
                edgePen.Dispose();
                framePen.Dispose();
                centered.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
