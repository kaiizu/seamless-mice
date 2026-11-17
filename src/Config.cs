using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SeamlessMice
{
    public sealed class Calibration
    {
        public double WidthMm, HeightMm, TopMm;
        public double LeftMm = Double.NaN;
    }
    public sealed class Configuration
    {
        public const int FormatVersion = 1;
        public const int MaxScreens = 64;
        public bool Enabled, PauseFullscreen = true;
        public readonly Dictionary<string, Calibration> Screens = new Dictionary<string, Calibration>(StringComparer.OrdinalIgnoreCase);
        public static string DefaultPath { get { return ResolvePath(AppDomain.CurrentDomain.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)); } }
        public static string ResolvePath(string applicationFolder, string profileFolder)
        {
            return File.Exists(Path.Combine(applicationFolder, "installed.flag")) ?
                Path.Combine(profileFolder, "SeamlessMice", "seamless-mice.ini") : Path.Combine(applicationFolder, "seamless-mice.ini");
        }
        public static Configuration Read(string path)
        {
            var config = new Configuration();
            if (!File.Exists(path)) return config;
            if (new FileInfo(path).Length > 65536) throw new FormatException("Settings file is too large.");
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length > 512) throw new FormatException("Settings file is too long.");
            string current = null;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[screen:") && line.EndsWith("]"))
                {
                    if (config.Screens.Count == MaxScreens) throw new FormatException("Too many screens in the settings file.");
                    current = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(8, line.Length - 9)));
                    if (current.Length == 0 || current.Length > 256 || current.IndexOf('\0') >= 0) throw new FormatException("Invalid screen identity.");
                    if (config.Screens.ContainsKey(current)) throw new FormatException("Duplicate screen identity.");
                    config.Screens.Add(current, new Calibration());
                    continue;
                }
                int equals = line.IndexOf('=');
                if (equals <= 0) throw new FormatException("Invalid settings line.");
                string key = line.Substring(0, equals).Trim(), value = line.Substring(equals + 1).Trim();
                if (current == null)
                {
                    if (key == "enabled") config.Enabled = ParseBool(value);
                    else if (key == "pause_fullscreen") config.PauseFullscreen = ParseBool(value);
                    else if (key == "version" && value != FormatVersion.ToString(CultureInfo.InvariantCulture)) throw new FormatException("Unsupported settings version.");
                }
                else
                {
                    double number = Double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (!Panel.Finite(number)) throw new FormatException("Screen values must be finite.");
                    Calibration c = config.Screens[current];
                    if (key == "width_mm") c.WidthMm = number;
                    else if (key == "height_mm") c.HeightMm = number;
                    else if (key == "top_mm") c.TopMm = number;
                    else if (key == "left_mm") c.LeftMm = number;
                }
            }
            foreach (Calibration c in config.Screens.Values)
                if (c.WidthMm < 50 || c.HeightMm < 50 || c.WidthMm > 5000 || c.HeightMm > 5000 || Math.Abs(c.TopMm) > 10000 || (!Double.IsNaN(c.LeftMm) && Math.Abs(c.LeftMm) > 10000))
                    throw new FormatException("Screen measurements are outside the supported range.");
            return config;
        }
        private static bool ParseBool(string value)
        {
            if (value == "true") return true;
            if (value == "false") return false;
            throw new FormatException("Expected true or false.");
        }
        public Layout MakeLayout(Display[] displays)
        {
            var panels = new Panel[displays.Length];
            for (int i = 0; i < displays.Length; i++)
            {
                Calibration c;
                if (!Screens.TryGetValue(displays[i].Id, out c))
                    throw new InvalidOperationException("A connected screen needs calibration. Open settings and apply the layout.");
                panels[i] = new Panel(displays[i].Id, displays[i].Name, displays[i].Pixels, c.WidthMm, c.HeightMm, c.TopMm, c.LeftMm);
            }
            return new Layout(panels);
        }
        public void Save(string path)
        {
            if (Screens.Count > MaxScreens) throw new InvalidOperationException("Too many screens to save.");
            StringBuilder text = new StringBuilder("# Seamless Mice portable settings. Measurements use millimetres.\r\nversion=" + FormatVersion + "\r\n");
            text.AppendLine("enabled=" + (Enabled ? "true" : "false"));
            text.AppendLine("pause_fullscreen=" + (PauseFullscreen ? "true" : "false"));
            var ordered = new List<KeyValuePair<string, Calibration>>(Screens);
            ordered.Sort(delegate(KeyValuePair<string, Calibration> a, KeyValuePair<string, Calibration> b) { return StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key); });
            foreach (KeyValuePair<string, Calibration> pair in ordered)
            {
                Calibration c = pair.Value;
                if (String.IsNullOrEmpty(pair.Key) || pair.Key.Length > 256 || pair.Key.IndexOf('\0') >= 0) throw new InvalidOperationException("Invalid screen identity.");
                // Validate before replacing the last known good settings file.
                new Panel(pair.Key, "", new PixelRect(0, 0, 100, 100), c.WidthMm, c.HeightMm, c.TopMm, c.LeftMm);
                text.AppendLine("[screen:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Key)) + "]");
                text.AppendLine("width_mm=" + c.WidthMm.ToString("R", CultureInfo.InvariantCulture));
                text.AppendLine("height_mm=" + c.HeightMm.ToString("R", CultureInfo.InvariantCulture));
                text.AppendLine("top_mm=" + c.TopMm.ToString("R", CultureInfo.InvariantCulture));
                if (!Double.IsNaN(c.LeftMm)) text.AppendLine("left_mm=" + c.LeftMm.ToString("R", CultureInfo.InvariantCulture));
            }
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text.ToString());
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
