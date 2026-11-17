using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SeamlessMice
{
    internal enum ThemePreference { System, Light, Dark }

    // Shared visual tokens for the settings editor. Native WinForms only; no UI framework.
    internal static class Theme
    {
        internal static bool IsDark { get; private set; }
        internal static ThemePreference Preference { get; private set; }

        internal static Color Page, Surface, SurfaceHover, Ink, Muted, Accent, AccentHover, AccentDown,
            AccentSoft, Border, BorderStrong, CanvasGrid, PanelEdge, PanelFill, Badge, Caution, Error,
            PrimaryFill, PrimaryFillHover, PrimaryFillDown, PrimaryText;

        internal static readonly Font Body = new Font("Segoe UI", 10f);
        internal static readonly Font Small = new Font("Segoe UI", 9f);
        internal static readonly Font Title = new Font("Segoe UI Semibold", 16.5f);
        internal static readonly Font Emphasis = new Font("Segoe UI Semibold", 9.5f);

        static Theme()
        {
            SetPreference(UiSettings.ReadPreference());
        }

        internal static void SetPreference(ThemePreference preference)
        {
            Preference = preference;
            ApplyPalette(preference == ThemePreference.Dark ||
                (preference == ThemePreference.System && SystemPrefersDark()));
        }

        internal static bool SystemPrefersDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return false;
                    object apps = key.GetValue("AppsUseLightTheme");
                    if (apps is int) return (int)apps == 0;
                    object system = key.GetValue("SystemUsesLightTheme");
                    if (system is int) return (int)system == 0;
                }
            }
            catch (Exception) { }
            return false;
        }

        private static void ApplyPalette(bool dark)
        {
            IsDark = dark;
            if (dark)
            {
                Page = Color.FromArgb(18, 23, 25);
                Surface = Color.FromArgb(25, 32, 35);
                SurfaceHover = Color.FromArgb(33, 42, 45);
                Ink = Color.FromArgb(232, 238, 239);
                Muted = Color.FromArgb(154, 168, 171);
                Accent = Color.FromArgb(78, 184, 175);
                AccentHover = Color.FromArgb(96, 203, 193);
                AccentDown = Color.FromArgb(58, 158, 151);
                AccentSoft = Color.FromArgb(27, 50, 51);
                Border = Color.FromArgb(48, 58, 61);
                BorderStrong = Color.FromArgb(74, 88, 91);
                CanvasGrid = Color.FromArgb(40, 49, 52);
                PanelEdge = Color.FromArgb(96, 113, 117);
                PanelFill = Color.FromArgb(31, 39, 42);
                Badge = Color.FromArgb(49, 61, 64);
                Caution = Color.FromArgb(232, 181, 88);
                Error = Color.FromArgb(242, 126, 116);
                PrimaryFill = Color.FromArgb(20, 122, 116);
                PrimaryFillHover = Color.FromArgb(24, 140, 133);
                PrimaryFillDown = Color.FromArgb(15, 103, 98);
                PrimaryText = Color.White;
            }
            else
            {
                Page = Color.FromArgb(247, 249, 250);
                Surface = Color.White;
                SurfaceHover = Color.FromArgb(247, 250, 250);
                Ink = Color.FromArgb(24, 40, 44);
                Muted = Color.FromArgb(95, 110, 114);
                Accent = Color.FromArgb(12, 105, 104);
                AccentHover = Color.FromArgb(10, 91, 90);
                AccentDown = Color.FromArgb(8, 78, 77);
                AccentSoft = Color.FromArgb(233, 245, 242);
                Border = Color.FromArgb(223, 229, 231);
                BorderStrong = Color.FromArgb(207, 216, 219);
                CanvasGrid = Color.FromArgb(229, 234, 236);
                PanelEdge = Color.FromArgb(170, 181, 185);
                PanelFill = Color.FromArgb(251, 252, 252);
                Badge = Color.FromArgb(228, 235, 237);
                Caution = Color.FromArgb(146, 96, 12);
                Error = Color.FromArgb(176, 48, 48);
                PrimaryFill = Accent;
                PrimaryFillHover = AccentHover;
                PrimaryFillDown = AccentDown;
                PrimaryText = Color.White;
            }
        }

        internal static Button CreateButton(string text, bool primary)
        {
            var button = new RoundedButton
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(0, primary ? 38 : 30),
                Padding = new Padding(primary ? 18 : 10, 0, primary ? 18 : 10, 0),
                Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Font = primary ? Emphasis : Body,
                Tag = primary ? "primary" : "secondary"
            };
            StyleButton(button, primary);
            return button;
        }

        internal static GraphicsPath RoundRect(RectangleF bounds, float radius)
        {
            float limit = Math.Min(bounds.Width, bounds.Height) / 2;
            if (radius > limit) radius = limit;
            var path = new GraphicsPath();
            if (radius <= 0.5f) { path.AddRectangle(bounds); return path; }
            float diameter = radius * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void RoundRegion(Control control, float radius)
        {
            if (control.Width <= 1 || control.Height <= 1) return;
            using (var path = RoundRect(new RectangleF(0, 0, control.Width, control.Height), radius))
            {
                Region previous = control.Region;
                control.Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        internal static void StyleButton(Button button, bool primary)
        {
            button.BackColor = primary ? PrimaryFill : Surface;
            button.ForeColor = primary ? PrimaryText : Ink;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = BorderStrong;
            button.FlatAppearance.MouseOverBackColor = primary ? PrimaryFillHover : SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = primary ? PrimaryFillDown : Border;
        }
    }

    // Persists editor-only preferences beside the calibration file.
    internal static class UiSettings
    {
        private static string FilePath
        {
            get
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(Configuration.DefaultPath));
                return Path.Combine(folder, "editor.ini");
            }
        }

        internal static ThemePreference ReadPreference()
        {
            try
            {
                if (!File.Exists(FilePath)) return ThemePreference.System;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line == "theme=dark") return ThemePreference.Dark;
                    if (line == "theme=light") return ThemePreference.Light;
                    if (line == "theme=system") return ThemePreference.System;
                }
            }
            catch (Exception) { }
            return ThemePreference.System;
        }

        internal static void SavePreference(ThemePreference preference)
        {
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "theme=" + (preference == ThemePreference.Dark ? "dark" : preference == ThemePreference.Light ? "light" : "system") + "\r\n");
            }
            catch (Exception) { }
        }
    }

    // A flat surface with a one-pixel border and WinUI-style rounded corners.
    internal sealed class CardPanel : System.Windows.Forms.Panel
    {
        internal CardPanel()
        {
            DoubleBuffered = true;
            BackColor = Theme.Surface;
            Padding = new Padding(14, 10, 14, 10);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Theme.RoundRegion(this, 8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width <= 1 || Height <= 1) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8))
            using (var border = new Pen(Theme.Border))
                e.Graphics.DrawPath(border, path);
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool hover, pressed;

        internal RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Theme.RoundRegion(this, 6);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool primary = (Tag as string) == "primary";
            Color fill = primary
                ? (pressed ? Theme.PrimaryFillDown : hover ? Theme.PrimaryFillHover : Theme.PrimaryFill)
                : (pressed ? Theme.Border : hover ? Theme.SurfaceHover : Theme.Surface);
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (!primary) using (var border = new Pen(Theme.BorderStrong)) g.DrawPath(border, path);
            }
            if (Focused)
                using (var path = Theme.RoundRect(new RectangleF(2.5f, 2.5f, Width - 5, Height - 5), 4.5f))
                using (var focus = new Pen(Theme.Accent, 1.2f))
                    g.DrawPath(focus, path);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class RoundedCheckBox : CheckBox
    {
        private bool hover;

        internal RoundedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font);
            return new Size(text.Width + 26, Math.Max(22, text.Height + 4));
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(BackColor)) g.FillRectangle(background, ClientRectangle);
            int top = Math.Max(0, (Height - 16) / 2);
            var box = new RectangleF(0.5f, top + 0.5f, 15, 15);
            using (var path = Theme.RoundRect(box, 4))
            {
                using (var fill = new SolidBrush(Checked ? Theme.PrimaryFill : (hover ? Theme.SurfaceHover : Theme.Surface))) g.FillPath(fill, path);
                using (var border = new Pen(Checked ? Theme.PrimaryFill : (hover || Focused ? Theme.Accent : Theme.BorderStrong)))
                    g.DrawPath(border, path);
            }
            if (Checked)
                using (var check = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(check, new[] { new Point(4, top + 8), new Point(7, top + 11), new Point(12, top + 5) });
            if (Focused)
                using (var path = Theme.RoundRect(new RectangleF(1.5f, top + 1.5f, 13, 13), 3))
                using (var focus = new Pen(Theme.Accent, 1f))
                    g.DrawPath(focus, path);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(22, 0, Math.Max(0, Width - 22), Height), Enabled ? Theme.Ink : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
