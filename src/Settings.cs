using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SeamlessMice
{
    internal sealed class SettingsForm : Form
    {
        private Display[] displays;
        private Configuration config;
        private readonly ScreenPicker selected = new ScreenPicker();
        private readonly NumericField width = Number(50, 5000), height = Number(50, 5000), left = Number(-10000, 10000), top = Number(-10000, 10000), diagonal = Number(5, 150);
        private readonly CheckBox enabled = new RoundedCheckBox(), fullscreen = new RoundedCheckBox(), startup = new RoundedCheckBox();
        private readonly Label message = new Label();
        private readonly LayoutCanvas canvas = new LayoutCanvas();
        private readonly Button apply = Theme.CreateButton("Apply layout", true);
        private readonly Button themeToggle = Theme.CreateButton("Dark theme", false);
        private bool loading;
        private string configurationError;
        private bool initialStartup;
        private MessageTone tone = MessageTone.Muted;

        private enum MessageTone { Muted, Caution, Accent, Error }

        private static NumericField Number(decimal min, decimal max)
        { return new NumericField { Minimum = min, Maximum = max, DecimalPlaces = 1, Increment = 1 }; }

        internal SettingsForm()
        {
            Text = "Seamless Mice - Settings";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = Theme.Body;
            BackColor = Theme.Page;
            ForeColor = Theme.Ink;
            ClientSize = new Size(940, 820);
            MinimumSize = new Size(956, 859);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            DoubleBuffered = true;
            Controls.Add(BuildContent());
            AcceptButton = apply;
            ApplyTheme();
            LoadScreens();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.ApplyDarkTitleBar(Handle, Theme.IsDark);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyTheme();
            // Some native input children are created after the form handle; recolor once more.
            BeginInvoke((MethodInvoker)delegate { ApplyTheme(); });
        }

        private Control BuildContent()
        {
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20), ColumnCount = 1, RowCount = 8, Margin = Padding.Empty, BackColor = Theme.Page };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            float[] rows = { 58, 0, 46, 46, 88, 40, 76, 56 };
            for (int i = 0; i < rows.Length; i++)
                content.RowStyles.Add(i == 1 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, rows[i]));
            content.Controls.Add(BuildHeader(), 0, 0);
            canvas.Dock = DockStyle.Fill;
            canvas.Margin = new Padding(0, 4, 0, 4);
            canvas.SelectionChanged += delegate(int index) { selected.SelectedIndex = index; };
            canvas.LayoutChanged += delegate { LoadSelection(); Dirty(); };
            content.Controls.Add(canvas, 0, 1);
            content.Controls.Add(BuildToolbar(), 0, 2);
            content.Controls.Add(BuildSelector(), 0, 3);
            content.Controls.Add(BuildMeasurements(), 0, 4);
            content.Controls.Add(BuildHint(), 0, 5);
            content.Controls.Add(BuildBehavior(), 0, 6);
            content.Controls.Add(BuildFooter(), 0, 7);
            return content;
        }

        private Control BuildHeader()
        {
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty, BackColor = Theme.Page };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
            var logo = new PictureBox { Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 4, 8, 0), BackColor = Color.Transparent };
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SeamlessMice.Logo.png"))
                if (stream != null) using (Image image = Image.FromStream(stream)) logo.Image = new Bitmap(image);
            header.Controls.Add(logo, 0, 0);
            header.SetRowSpan(logo, 2);
            header.Controls.Add(new Label
            {
                Text = "Seamless Mice",
                Font = Theme.Title,
                ForeColor = Theme.Ink,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft,
                Margin = Padding.Empty
            }, 1, 0);
            header.Controls.Add(new Label
            {
                Text = "Arrange your displays as they sit on your desk",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(1, 0, 0, 0),
                Tag = "muted"
            }, 1, 1);
            themeToggle.Anchor = AnchorStyles.Right;
            themeToggle.Margin = new Padding(12, 0, 0, 0);
            themeToggle.AccessibleName = "Toggle dark theme";
            themeToggle.Click += delegate { ToggleTheme(); };
            header.Controls.Add(themeToggle, 2, 0);
            header.SetRowSpan(themeToggle, 2);
            return header;
        }

        private Control BuildToolbar()
        {
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Page };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var align = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Page };
            align.Controls.Add(new Label { Text = "Align edges", AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 12, 0), Tag = "muted" });
            foreach (string name in new[] { "Top", "Center", "Bottom" })
            {
                string alignmentName = name;
                var button = Theme.CreateButton(name, false);
                button.Margin = new Padding(0, 0, 6, 0);
                button.Click += delegate { AlignScreens(alignmentName); };
                align.Controls.Add(button);
            }
            toolbar.Controls.Add(align, 0, 0);
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Page };
            var import = Theme.CreateButton("Import layout...", false);
            import.Margin = new Padding(8, 0, 0, 0);
            import.Click += delegate { ImportLayout(); };
            var fit = Theme.CreateButton("Fit view", false);
            fit.Margin = Padding.Empty;
            fit.Click += delegate { canvas.Fit(); };
            tools.Controls.Add(import);
            tools.Controls.Add(fit);
            toolbar.Controls.Add(tools, 1, 0);
            return toolbar;
        }

        private Control BuildSelector()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Page };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(new Label { Text = "Screen", AutoSize = true, Font = Theme.Body, ForeColor = Theme.Ink, Margin = new Padding(0, 9, 12, 0) }, 0, 0);
            selected.Font = Theme.Body;
            selected.BackColor = Theme.Surface;
            selected.ForeColor = Theme.Ink;
            selected.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            selected.Margin = new Padding(0, 4, 10, 0);
            selected.SelectedIndexChanged += delegate { LoadSelection(); };
            row.Controls.Add(selected, 1, 0);
            var refresh = Theme.CreateButton("Re-detect", false);
            refresh.Margin = Padding.Empty;
            refresh.Click += delegate { CaptureEdits(); LoadScreens(); };
            row.Controls.Add(refresh, 2, 0);
            return row;
        }

        private Control BuildMeasurements()
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            var measurements = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Margin = Padding.Empty, BackColor = Theme.Surface };
            measurements.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
            measurements.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            string[] labels = { "Diagonal (in)", "Width (mm)", "Height (mm)", "Left (mm)", "Top (mm)" };
            NumericField[] controls = { diagonal, width, height, left, top };
            for (int i = 0; i < controls.Length; i++)
            {
                measurements.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
                measurements.Controls.Add(new Label { Text = labels[i], AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 14, 0), Tag = "muted" }, i, 0);
                controls[i].Font = Theme.Body;
                controls[i].AccessibleName = labels[i];
                controls[i].BackColor = Theme.Surface;
                controls[i].ForeColor = Theme.Ink;
                controls[i].Anchor = AnchorStyles.Left | AnchorStyles.Right;
                controls[i].Margin = new Padding(0, 2, i == controls.Length - 1 ? 0 : 14, 0);
                measurements.Controls.Add(controls[i], i, 1);
            }
            width.ValueChanged += MeasurementChanged;
            height.ValueChanged += MeasurementChanged;
            left.ValueChanged += MeasurementChanged;
            top.ValueChanged += MeasurementChanged;
            diagonal.ValueChanged += DiagonalChanged;
            card.Controls.Add(measurements);
            return card;
        }

        private Control BuildHint()
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Padding = new Padding(2, 6, 0, 0),
                Tag = "muted",
                Text = "Drag a screen to move it  /  Arrow keys: 1 mm  /  Shift + arrows: 10 mm\r\n" +
                    "Hold Alt while dragging to bypass snapping  /  Esc: cancel  /  Shift at an edge: Windows crossing"
            };
        }

        private Control BuildBehavior()
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4), Padding = new Padding(14, 8, 14, 8) };
            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Theme.Surface };
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            stack.Controls.Add(new Label { Text = "Behavior", AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = Padding.Empty, Tag = "muted" }, 0, 0);
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = false, Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 0), BackColor = Theme.Surface };
            enabled.Text = "Enable physical-height cursor correction";
            fullscreen.Text = "Use Windows cursor behavior in full-screen apps";
            startup.Text = "Start with Windows";
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                initialStartup = run != null && run.GetValue("SeamlessMice") != null;
            startup.Checked = initialStartup;
            foreach (CheckBox option in new[] { enabled, fullscreen, startup })
            {
                option.AutoSize = true;
                option.Font = Theme.Body;
                option.ForeColor = Theme.Ink;
                option.Margin = new Padding(0, 0, 28, 0);
                option.CheckedChanged += OptionChanged;
                options.Controls.Add(option);
            }
            stack.Controls.Add(options, 0, 1);
            card.Controls.Add(stack);
            return card;
        }

        private Control BuildFooter()
        {
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 4, 0, 0), BackColor = Theme.Page };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
            message.Dock = DockStyle.Fill;
            message.Font = Theme.Small;
            message.ForeColor = Theme.Muted;
            message.TextAlign = ContentAlignment.MiddleLeft;
            message.Padding = new Padding(2, 0, 12, 0);
            message.AutoEllipsis = true;
            SetMessage("Adjust the layout to match your desk, then apply.", MessageTone.Muted);
            footer.Controls.Add(message, 0, 0);
            apply.AutoSize = false;
            apply.Dock = DockStyle.Fill;
            apply.Margin = new Padding(0, 6, 0, 6);
            apply.Click += delegate { ApplyLayout(); };
            footer.Controls.Add(apply, 1, 0);
            return footer;
        }

        private void SetMessage(string text, MessageTone value)
        {
            tone = value;
            message.Text = text;
            message.ForeColor = ToneColor();
        }

        private Color ToneColor()
        {
            return tone == MessageTone.Caution ? Theme.Caution :
                tone == MessageTone.Accent ? Theme.Accent :
                tone == MessageTone.Error ? Theme.Error : Theme.Muted;
        }

        private void ToggleTheme()
        {
            Theme.SetPreference(Theme.IsDark ? ThemePreference.Light : ThemePreference.Dark);
            UiSettings.SavePreference(Theme.Preference);
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            BackColor = Theme.Page;
            ForeColor = Theme.Ink;
            themeToggle.Text = Theme.IsDark ? "Light theme" : "Dark theme";
            ApplyTheme(this, Theme.Page);
            if (IsHandleCreated) Native.ApplyDarkTitleBar(Handle, Theme.IsDark);
            Invalidate(true);
        }

        private void ApplyTheme(Control control, Color background)
        {
            if (control is Form)
            {
                control.BackColor = Theme.Page;
                control.ForeColor = Theme.Ink;
                background = Theme.Page;
            }
            else if (control is CardPanel)
            {
                control.BackColor = Theme.Surface;
                background = Theme.Surface;
            }
            else if (control is LayoutCanvas)
            {
                control.BackColor = Theme.Surface;
                ((LayoutCanvas)control).ApplyPalette();
                background = Theme.Surface;
            }
            else if (control is Label)
            {
                control.BackColor = background;
                control.ForeColor = ReferenceEquals(control, message) ? ToneColor() :
                    (control.Tag as string) == "muted" ? Theme.Muted : Theme.Ink;
            }
            else if (control is Button)
            {
                Theme.StyleButton((Button)control, (control.Tag as string) == "primary");
            }
            else if (control is CheckBox || control is NumericField || control is ScreenPicker)
            {
                control.BackColor = Theme.Surface;
                control.ForeColor = Theme.Ink;
                foreach (Control child in control.Controls)
                {
                    child.BackColor = Theme.Surface;
                    child.ForeColor = Theme.Ink;
                }
            }
            else if (control is TableLayoutPanel || control is FlowLayoutPanel || control is PictureBox)
            {
                control.BackColor = background;
            }
            foreach (Control child in control.Controls) ApplyTheme(child, background);
        }

        private void LoadScreens()
        {
            try
            {
                string previousId = selected.SelectedIndex >= 0 && displays != null && selected.SelectedIndex < displays.Length ? displays[selected.SelectedIndex].Id : null;
                displays = Displays.Enumerate();
                if (config == null)
                {
                    try { config = Configuration.Read(Configuration.DefaultPath); }
                    catch (Exception error)
                    {
                        // Keep the editor usable when the engine has rejected a damaged file.
                        config = new Configuration(); configurationError = error.Message;
                    }
                    // Activation is an explicit Apply action. First-run calibration starts checked.
                    loading = true;
                    enabled.Checked = config.Enabled || !File.Exists(Configuration.DefaultPath);
                    fullscreen.Checked = config.PauseFullscreen;
                    loading = false;
                }
                double nextLeft = 0;
                foreach (Display d in displays)
                {
                    if (!config.Screens.ContainsKey(d.Id)) config.Screens[d.Id] = new Calibration { WidthMm = d.WidthMm, HeightMm = d.HeightMm, TopMm = 0 };
                    Calibration c = config.Screens[d.Id];
                    if (Double.IsNaN(c.LeftMm)) c.LeftMm = nextLeft;
                    nextLeft = c.LeftMm + c.WidthMm;
                }
                var options = new List<string>();
                for (int i = 0; i < displays.Length; i++)
                    options.Add((i + 1) + "  " + displays[i].Name + (displays[i].HasPhysicalSize ? "" : " (estimated)") + "  /  " + displays[i].Pixels.Width + " x " + displays[i].Pixels.Height);
                loading = true;
                selected.SetItems(options);
                loading = false;
                canvas.Displays = displays;
                canvas.Config = config;
                canvas.Fit();
                if (displays.Length > 0)
                {
                    int index = 0;
                    if (previousId != null)
                        for (int i = 0; i < displays.Length; i++) if (String.Equals(displays[i].Id, previousId, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
                    selected.SelectedIndex = index;
                }
                else { canvas.Selected = -1; canvas.Invalidate(); }
                SetMessage(configurationError != null ? "Settings need repair. Check the measurements and apply to replace the file." :
                    displays.Length < 2 ? "Connect a second screen to use correction." : "Adjust the layout to match your desk, then apply.",
                    configurationError != null ? MessageTone.Caution : MessageTone.Muted);
            }
            catch (Exception error)
            {
                loading = false;
                SetMessage(error.Message, MessageTone.Error);
            }
        }

        private void LoadSelection()
        {
            if (loading || selected.SelectedIndex < 0 || displays == null || config == null) return;
            Display d = displays[selected.SelectedIndex];
            Calibration c = config.Screens[d.Id];
            loading = true;
            width.Value = (decimal)c.WidthMm;
            height.Value = (decimal)c.HeightMm;
            left.Value = (decimal)c.LeftMm;
            top.Value = (decimal)c.TopMm;
            diagonal.Value = DiagonalValue(c);
            loading = false;
            canvas.Selected = selected.SelectedIndex;
            canvas.Invalidate();
        }

        private decimal DiagonalValue(Calibration c)
        {
            double inches = Math.Sqrt(c.WidthMm * c.WidthMm + c.HeightMm * c.HeightMm) / 25.4;
            return Math.Min(diagonal.Maximum, Math.Max(diagonal.Minimum, (decimal)inches));
        }

        private void CaptureEdits()
        {
            if (loading || config == null || displays == null || selected.SelectedIndex < 0) return;
            Calibration c = config.Screens[displays[selected.SelectedIndex].Id];
            c.WidthMm = (double)width.Value;
            c.HeightMm = (double)height.Value;
            c.LeftMm = (double)left.Value;
            c.TopMm = (double)top.Value;
        }

        private void MeasurementChanged(object sender, EventArgs e)
        {
            if (loading) return;
            CaptureEdits();
            loading = true;
            double inches = Math.Sqrt((double)(width.Value * width.Value + height.Value * height.Value)) / 25.4;
            diagonal.Value = Math.Min(diagonal.Maximum, Math.Max(diagonal.Minimum, (decimal)inches));
            loading = false;
            canvas.Invalidate();
            Dirty();
        }

        private void DiagonalChanged(object sender, EventArgs e)
        {
            if (loading || selected.SelectedIndex < 0 || displays == null) return;
            Display d = displays[selected.SelectedIndex];
            loading = true;
            width.Value = (decimal)Panel.WidthFromDiagonal((double)diagonal.Value, d.Pixels);
            height.Value = (decimal)Panel.HeightFromDiagonal((double)diagonal.Value, d.Pixels);
            loading = false;
            CaptureEdits();
            canvas.Fit();
            Dirty();
        }

        private void AlignScreens(string alignment)
        {
            if (displays == null || displays.Length == 0 || config == null) return;
            CaptureEdits();
            double maxHeight = 0;
            foreach (Display d in displays) maxHeight = Math.Max(maxHeight, config.Screens[d.Id].HeightMm);
            foreach (Display d in displays)
            {
                Calibration c = config.Screens[d.Id];
                c.TopMm = alignment == "Top" ? 0 : (maxHeight - c.HeightMm) / (alignment == "Center" ? 2 : 1);
            }
            LoadSelection();
            canvas.Fit();
            Dirty();
        }

        private void Dirty()
        {
            SetMessage("Unapplied changes. Apply when the layout matches your desk.", MessageTone.Caution);
        }

        private void OptionChanged(object sender, EventArgs e)
        {
            if (!loading) Dirty();
        }

        private void ImportLayout()
        {
            using (var dialog = new OpenFileDialog { Title = "Import a Seamless Mice layout", Filter = "Layout files (*.ini)|*.ini|All files (*.*)|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try
                    {
                        config = Configuration.Read(dialog.FileName);
                        configurationError = null;
                        loading = true;
                        enabled.Checked = config.Enabled;
                        fullscreen.Checked = config.PauseFullscreen;
                        loading = false;
                        LoadScreens();
                        Dirty();
                    }
                    catch (Exception error)
                    {
                        loading = false;
                        SetMessage(error.Message, MessageTone.Error);
                    }
        }

        private void ApplyLayout()
        {
            try
            {
                CaptureEdits();
                config.Enabled = enabled.Checked;
                config.PauseFullscreen = fullscreen.Checked;
                if (displays == null || displays.Length == 0) throw new InvalidOperationException("No screens are connected.");
                config.MakeLayout(displays);
                config.Save(Configuration.DefaultPath);
                if (startup.Checked != initialStartup)
                    using (RegistryKey run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        if (startup.Checked) run.SetValue("SeamlessMice", "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SeamlessMice.exe") + "\" --background");
                        else run.DeleteValue("SeamlessMice", false);
                        initialStartup = startup.Checked;
                    }
                configurationError = null;
                IntPtr engine = Native.FindWindowW(Native.WindowClass, null);
                if (engine == IntPtr.Zero)
                    Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SeamlessMice.exe"), "--background") { UseShellExecute = false });
                else Native.PostMessageW(engine, Native.WM_APP_RELOAD, IntPtr.Zero, IntPtr.Zero);
                SetMessage("Saved. " + (config.Enabled ? "The engine will apply the layout." : "Correction is disabled."), MessageTone.Accent);
            }
            catch (Exception error)
            {
                SetMessage(error.Message, MessageTone.Error);
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Native.EnableDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 1 && args[0] == "--editor-checks") return EditorChecks.Run();
            if (args.Length >= 2 && args[0] == "--preview")
            {
                if (args.Length > 2)
                {
                    if (args[2] == "dark") Theme.SetPreference(ThemePreference.Dark);
                    else if (args[2] == "light") Theme.SetPreference(ThemePreference.Light);
                    else if (args[2] != "system") return 2;
                    else Theme.SetPreference(ThemePreference.System);
                }
                using (var form = new SettingsForm())
                {
                    // Render this application's own controls offscreen, without desktop capture.
                    form.ShowInTaskbar = false;
                    form.Opacity = 0;
                    form.Show();
                    form.PerformLayout();
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(args[1]);
                    }
                }
                return 0;
            }
            bool owner;
            using (var mutex = new Mutex(true, @"Local\SeamlessMice.Settings.v1", out owner))
            {
                if (!owner)
                {
                    IntPtr existing = Native.FindWindowW(null, "Seamless Mice - Settings");
                    if (existing != IntPtr.Zero) Native.SetForegroundWindow(existing);
                    return 0;
                }
                Application.Run(new SettingsForm());
            }
            return 0;
        }
    }
}
