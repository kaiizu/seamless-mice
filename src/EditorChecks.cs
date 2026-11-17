using System;

namespace SeamlessMice
{
    internal static class EditorChecks
    {
        internal static int Run()
        {
            try
            {
                var config = new Configuration();
                config.Screens["a"] = new Calibration { WidthMm = 345, HeightMm = 215, LeftMm = 0, TopMm = 60 };
                config.Screens["b"] = new Calibration { WidthMm = 597, HeightMm = 336, LeftMm = 345, TopMm = 0 };
                using (var canvas = new LayoutCanvas { Config = config, Displays = new[] { new Display { Id = "a" }, new Display { Id = "b" } }, Selected = 0 })
                {
                    canvas.Nudge(1, 0, false);
                    if (config.Screens["a"].LeftMm != 1) throw new Exception("Right arrow must move 1 mm.");
                    canvas.Nudge(0, -1, true);
                    if (config.Screens["a"].TopMm != 50) throw new Exception("Shift+up must move 10 mm.");
                    canvas.DragTo(-33.34, 12.25, false);
                    if (config.Screens["a"].LeftMm != -33.3 || config.Screens["a"].TopMm != 12.2) throw new Exception("Drag coordinate precision failed.");
                    canvas.DragTo(-0.1, 0.2, true);
                    if (config.Screens["a"].LeftMm != 0 || config.Screens["a"].TopMm != 0) throw new Exception("Edge snapping failed.");
                    canvas.Nudge(-1, 0, true);
                    if (config.Screens["a"].LeftMm != -10) throw new Exception("Shift+left failed.");
                }
                using (var field = new NumericField { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 1 })
                {
                    field.Value = 10.5m;
                    if (field.Value != 10.5m) throw new Exception("Numeric field value failed.");
                    field.Value = 200m;
                    if (field.Value != 100m) throw new Exception("Numeric field maximum failed.");
                }
                using (var picker = new ScreenPicker())
                {
                    bool changed = false;
                    picker.SelectedIndexChanged += delegate { changed = true; };
                    picker.SetItems(new[] { "one", "two" });
                    picker.SelectedIndex = 1;
                    if (picker.SelectedIndex != 1 || !changed) throw new Exception("Screen picker selection failed.");
                }
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
