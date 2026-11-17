using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace SeamlessMice
{
    internal static class Checks
    {
        private static int assertions;
        private static void Assert(bool truth, string description)
        { assertions++; if (!truth) throw new Exception(description); }
        private static void Throws(Action action, string description)
        {
            bool threw = false;
            try { action(); } catch (ArgumentException) { threw = true; } catch (FormatException) { threw = true; }
            Assert(threw, description);
        }
        private static Layout Example()
        {
            return new Layout(new[] {
                new Panel("laptop", "16-inch 16:10 laptop", new PixelRect(-2560, 300, 0, 1900), 344.6, 215.4, 100),
                new Panel("main", "27-inch 16:9 monitor", new PixelRect(0, 0, 2560, 1440), 597.7, 336.2, 0)
            });
        }
        private static void GeometryChecks()
        {
            Layout layout = Example();
            Transition t = layout.Move(0, new PixelPoint(-100, 500));
            Assert(!t.Reposition && t.Screen == 0, "Ordinary movement must pass through.");
            t = layout.Move(0, new PixelPoint(0, 1100));
            Assert(t.Reposition && t.Screen == 1 && t.Point.X == 0, "Left to right seam.");
            double expectedMm = 100 + 800 * (215.4 / 1600);
            Assert(Math.Abs(t.Point.Y * (336.2 / 1440) - expectedMm) < 336.2 / 1440, "Physical height, including Windows and physical vertical offsets.");
            Transition back = layout.Move(1, new PixelPoint(-1, t.Point.Y));
            Assert(back.Screen == 0 && Math.Abs(back.Point.Y - 1100) <= 2, "Round-trip accuracy is within destination pixel rounding.");
            t = layout.Move(1, new PixelPoint(-1, 10));
            Assert(t.Reposition && t.Screen == 1 && t.Point.X == 0, "Exposed upper portion of larger screen must block crossing.");
            t = layout.Move(1, new PixelPoint(-1, 1439));
            Assert(t.Reposition && t.Screen == 1, "Exposed lower portion of larger screen must block crossing.");
            t = layout.Move(0, new PixelPoint(40, 1100));
            Assert(t.Screen == 1 && t.Point.X > 0 && t.Point.X < 40, "Overshoot scales with pixel density.");
            t = layout.Move(0, new PixelPoint(Int32.MaxValue, 1100));
            Assert(t.Screen == 1 && t.Point.X == 2559, "Huge deltas remain inside destination screen.");
            t = layout.Move(1, new PixelPoint(Int32.MinValue, 800));
            Assert(t.Screen == 0 && t.Point.X == -2560, "Huge negative deltas do not overflow.");
            Assert(layout.Move(0, new PixelPoint(-3000, 1100)).Screen == 0, "Outer left edge cannot cross.");
            Assert(layout.Move(1, new PixelPoint(3000, 500)).Screen == 1, "Outer right edge cannot cross.");
            Assert(layout.Move(0, new PixelPoint(-500, -10)).Screen == 0, "Unconnected vertical edge cannot cross.");
            Assert(layout.Find(new PixelPoint(-1, 500)) == 0 && layout.Find(new PixelPoint(0, 500)) == 1, "Exclusive pixel edges.");
            Assert(layout.Find(new PixelPoint(-2000, 299)) == -1, "Offscreen point detection.");
            Assert(!layout.Move(-1, new PixelPoint(100, 100)).Reposition, "Unknown source passes through.");
            var vertical = new Layout(new[] {
                new Panel("top", "Top", new PixelRect(0,0,1000,1000), 200, 200, 0, 0),
                new Panel("bottom", "Bottom", new PixelRect(1000,0,2000,1000), 200, 200, 250, 0)
            });
            int topIndex = vertical.Panels[0].Id == "top" ? 0 : 1, bottomIndex = 1 - topIndex;
            Transition down = vertical.Move(topIndex, new PixelPoint(500, 1000));
            Assert(down.Screen == bottomIndex && down.Point.X == 1500 && down.Point.Y == 0, "Dragging a panel below another routes through the bottom edge, including a physical gap.");
            Transition up = vertical.Move(bottomIndex, new PixelPoint(1500, -1));
            Assert(up.Screen == topIndex && up.Point.X == 500 && up.Point.Y == 999, "Vertical crossings work in both directions.");
            var reversed = new Layout(new[] {
                new Panel("first", "First", new PixelRect(0,0,1000,1000), 200, 200, 0, 220),
                new Panel("second", "Second", new PixelRect(1000,0,2000,1000), 200, 200, 0, 0)
            });
            Assert(reversed.Move(0, new PixelPoint(-1, 500)).Screen == 1, "Routing follows physical positions after a horizontal drag, independently of Windows pixel order.");
            Throws(delegate { new Layout(new[] { new Panel("a","",new PixelRect(0,0,1000,1000),200,200,0,0), new Panel("b","",new PixelRect(1000,0,2000,1000),200,200,50,50) }); }, "Overlapping physical panels cannot be applied.");
            var one = new Layout(new[] { layout.Panels[1] });
            Assert(one.Move(0, new PixelPoint(-1, 100)).Screen == 0, "Single-monitor layout cannot cross.");
            Throws(delegate { new Panel("", "", new PixelRect(0, 0, 100, 100), Double.NaN, 100, 0); }, "Reject NaN sizes.");
            Throws(delegate { new Panel("", "", new PixelRect(0, 0, 100, 100), 100, 100, Double.PositiveInfinity); }, "Reject infinite offsets.");
            Throws(delegate { new Layout(new Panel[0]); }, "Reject empty layout.");
            Throws(delegate { new Layout(new[] { layout.Panels[1], layout.Panels[1] }); }, "Reject stacked/overlapping horizontal extents.");
            double width = Panel.WidthFromDiagonal(16, new PixelRect(0, 0, 2560, 1600));
            double height = Panel.HeightFromDiagonal(16, new PixelRect(0, 0, 2560, 1600));
            Assert(Math.Abs(Math.Sqrt(width * width + height * height) - 16 * 25.4) < 0.000001, "16:10 diagonal conversion.");
            // Sweep every vertical pixel on both screens; verify containment and physical correspondence.
            for (int src = 0; src < 2; src++)
            {
                Panel from = layout.Panels[src], to = layout.Panels[1 - src];
                for (int y = from.Pixels.Top; y < from.Pixels.Bottom; y++)
                {
                    double mm = from.TopMm + (y - from.Pixels.Top) * from.MmPerPixelY;
                    t = layout.Move(src, new PixelPoint(src == 0 ? from.Pixels.Right : from.Pixels.Left - 1, y));
                    Assert(layout.Panels[t.Screen].Pixels.Contains(t.Point), "Every correction lands inside a real screen.");
                    if (mm >= to.TopMm && mm < to.TopMm + to.HeightMm)
                    {
                        Assert(t.Screen == 1 - src, "Every physically connected seam pixel crosses.");
                        double actual = to.TopMm + (t.Point.Y - to.Pixels.Top) * to.MmPerPixelY;
                        Assert(Math.Abs(actual - mm) < to.MmPerPixelY + 0.000001, "Height error is bounded by one target pixel.");
                    }
                    else Assert(t.Screen == src, "Physically disconnected seam pixels remain on the source.");
                }
            }
        }
        private static void ConfigChecks()
        {
            string path = Path.Combine(Path.GetTempPath(), "seamless-mice-check-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                Assert(!Configuration.Read(path).Enabled, "First run must be disabled until calibration.");
                Configuration config = new Configuration { Enabled = true, PauseFullscreen = false };
                config.Screens["MONITOR\\Acer/with=unicode-\u00f1"] = new Calibration { WidthMm = 344.65, HeightMm = 215.4, TopMm = -39.3, LeftMm = -120.5 };
                CultureInfo original = System.Threading.Thread.CurrentThread.CurrentCulture;
                try
                {
                    System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
                    config.Save(path);
                    Configuration read = Configuration.Read(path);
                    Assert(read.Enabled && !read.PauseFullscreen, "Boolean round trip.");
                    Assert(read.Screens["MONITOR\\Acer/with=unicode-\u00f1"].WidthMm == 344.65, "Locale-independent decimal and identity round trip.");
                    Assert(read.Screens["MONITOR\\Acer/with=unicode-\u00f1"].LeftMm == -120.5, "Dragged horizontal position survives saving and loading.");
                    config.Save(path);
                    Assert(File.Exists(path + ".bak"), "Atomic replacement preserves the previous configuration.");
                }
                finally { System.Threading.Thread.CurrentThread.CurrentCulture = original; }
                string good = File.ReadAllText(path);
                File.WriteAllText(path, good.Replace("344.65", "NaN"));
                Throws(delegate { Configuration.Read(path); }, "Reject non-finite config.");
                File.WriteAllText(path, "enabled=maybe");
                Throws(delegate { Configuration.Read(path); }, "Reject malformed boolean.");
                File.WriteAllText(path, "version=99");
                Throws(delegate { Configuration.Read(path); }, "Reject future unsupported versions.");
                File.WriteAllText(path, good + good.Substring(good.IndexOf("[screen:", StringComparison.Ordinal)));
                Throws(delegate { Configuration.Read(path); }, "Reject duplicate physical screen identities.");
                var missing = new Configuration();
                bool threw = false;
                try { missing.MakeLayout(new[] { new Display { Id = "unknown" } }); } catch (InvalidOperationException) { threw = true; }
                Assert(threw, "Unknown hot-plugged display must require calibration.");
                string fakeApplication = Path.Combine(Path.GetTempPath(), "seamless-installed-check-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(fakeApplication);
                try
                {
                    Assert(Configuration.ResolvePath(fakeApplication, "profile") == Path.Combine(fakeApplication,"seamless-mice.ini"), "Portable mode keeps settings beside the executable.");
                    File.WriteAllText(Path.Combine(fakeApplication,"installed.flag"), "installed");
                    Assert(Configuration.ResolvePath(fakeApplication, "profile") == Path.Combine("profile","SeamlessMice","seamless-mice.ini"), "Program Files installation stores settings in the per-user profile.");
                }
                finally { File.Delete(Path.Combine(fakeApplication,"installed.flag")); Directory.Delete(fakeApplication); }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }
        private static void Benchmark()
        {
            Layout layout = Example(); int checksum = 0;
            for (int i = 0; i < 10000; i++) checksum += layout.Move(0, new PixelPoint(-200, 900)).Screen;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            int collections = GC.CollectionCount(0);
            Stopwatch watch = Stopwatch.StartNew();
            const int count = 10000000;
            for (int i = 0; i < count; i++) checksum += layout.Move(0, new PixelPoint(-200 + (i & 127), 900)).Screen;
            watch.Stop();
            Console.WriteLine("Geometry interior: {0:N1} ns/event, {1:N0} iterations, Gen0 collections: {2}", watch.Elapsed.TotalMilliseconds * 1000000 / count, count, GC.CollectionCount(0) - collections);
            collections = GC.CollectionCount(0); watch.Restart();
            for (int i = 0; i < count; i++) checksum += layout.Move(0, new PixelPoint(i & 7, 900)).Point.Y;
            watch.Stop();
            Console.WriteLine("Geometry seam: {0:N1} ns/event, {1:N0} iterations, Gen0 collections: {2}", watch.Elapsed.TotalMilliseconds * 1000000 / count, count, GC.CollectionCount(0) - collections);
            Console.WriteLine("Checksum: {0}. Geometry only; excludes Windows hook delivery, SetCursorPos and other OS calls.", checksum);
        }
        private static void DescribeDisplays()
        {
            Native.EnableDpi();
            foreach (Display d in Displays.Enumerate())
                Console.WriteLine("{0}: {1}x{2} at ({3},{4}); {5:0.0} x {6:0.0} mm, {7:0.0} inches; EDID={8}; ID={9}",
                    d.Name, d.Pixels.Width, d.Pixels.Height, d.Pixels.Left, d.Pixels.Top, d.WidthMm, d.HeightMm, d.Diagonal, d.HasPhysicalSize, d.Id);
        }
        private static ulong EngineMessage(IntPtr window, uint command, int parameter)
        {
            UIntPtr result;
            if (Native.SendMessageTimeoutW(window, command, new IntPtr(parameter), IntPtr.Zero, 2, 3000, out result) == IntPtr.Zero)
                throw new Exception("The engine did not respond to a control message.");
            return result.ToUInt64();
        }
        private static void EngineSmoke()
        {
            Native.EnableDpi();
            if (Native.FindWindowW(Native.WindowClass, null) != IntPtr.Zero)
                throw new Exception("Exit the running Seamless Mice instance before the engine smoke check.");
            string bin = AppDomain.CurrentDomain.BaseDirectory;
            string folder = Path.GetFullPath(Path.Combine(bin, "..", "artifacts", "engine-smoke"));
            Directory.CreateDirectory(folder);
            foreach (string file in new[] { "SeamlessMice.exe", "SeamlessMice.Core.dll" })
                File.Copy(Path.Combine(bin, file), Path.Combine(folder, file), true);
            string path = Path.Combine(folder, "seamless-mice.ini");
            var config = new Configuration();
            Display[] displays = Displays.Enumerate();
            Assert(displays.Length >= 2, "Engine smoke needs at least two connected screens.");
            foreach (Display d in displays)
                config.Screens[d.Id] = new Calibration {
                    // Match Windows pixel geometry during the smoke check, avoiding a change to
                    // the user's physical calibration while testing the real hook lifecycle.
                    WidthMm = d.Pixels.Width * 0.25, HeightMm = d.Pixels.Height * 0.25, TopMm = d.Pixels.Top * 0.25 };
            config.Save(path);
            var start = new ProcessStartInfo(Path.Combine(folder, "SeamlessMice.exe"), "--background") {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            using (Process process = Process.Start(start))
            {
                IntPtr window = IntPtr.Zero;
                try
                {
                    Stopwatch startup = Stopwatch.StartNew();
                    while (window == IntPtr.Zero && !process.HasExited && startup.ElapsedMilliseconds < 5000)
                    { Thread.Sleep(25); window = Native.FindWindowW(Native.WindowClass, null); }
                    Assert(window != IntPtr.Zero, "Engine starts and owns its message window.");
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 0, "Unconfigured/disabled engine does not correct input.");
                    config.Enabled = true; config.Save(path);
                    EngineMessage(window, Native.WM_APP_RELOAD, 0);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 1, "Real mouse hook installs successfully.");
                    EngineMessage(window, Native.WM_APP_PAUSE, 0);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 2, "Pause detaches the hook.");
                    EngineMessage(window, Native.WM_DISPLAYCHANGE, 0); Thread.Sleep(850);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 2, "Manual pause survives display changes.");
                    EngineMessage(window, Native.WM_APP_RESUME, 0);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 1, "Resume reinstalls the hook.");
                    EngineMessage(window, Native.WM_WTSSESSION_CHANGE, 7);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 0, "Session lock detaches the hook.");
                    EngineMessage(window, Native.WM_APP_RESUME, 0);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 0, "Manual resume cannot override a locked session.");
                    EngineMessage(window, Native.WM_WTSSESSION_CHANGE, 8); Thread.Sleep(850);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 1, "Unlock notification restores correction.");
                    EngineMessage(window, Native.WM_POWERBROADCAST, 4);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 0, "Suspend notification detaches the hook.");
                    EngineMessage(window, Native.WM_POWERBROADCAST, 18); Thread.Sleep(850);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 1, "Resume notification restores correction.");
                    EngineMessage(window, Native.WM_APP_PAUSE, 0);
                    EngineMessage(window, Native.WM_WTSSESSION_CHANGE, 7);
                    EngineMessage(window, Native.WM_WTSSESSION_CHANGE, 8); Thread.Sleep(850);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 2, "Manual pause survives lock/unlock notifications.");
                    EngineMessage(window, Native.WM_POWERBROADCAST, 4);
                    EngineMessage(window, Native.WM_POWERBROADCAST, 18); Thread.Sleep(850);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 2, "Manual pause survives suspend/resume notifications.");
                    EngineMessage(window, Native.WM_APP_RESUME, 0);
                    using (Process duplicate = Process.Start(start))
                    { Assert(duplicate.WaitForExit(3000) && duplicate.ExitCode == 0, "Duplicate engine exits without installing a second hook."); }
                    // Observe the running engine without injecting cursor input or trimming memory.
                    Thread.Sleep(1500); process.Refresh();
                    double beforeCpu = process.TotalProcessorTime.TotalMilliseconds;
                    ulong beforeEvents = EngineMessage(window, Native.WM_APP_QUERY, 1);
                    Stopwatch sample = Stopwatch.StartNew(); Thread.Sleep(15000); sample.Stop(); process.Refresh();
                    double cpuMs = process.TotalProcessorTime.TotalMilliseconds - beforeCpu;
                    ulong inputCount = EngineMessage(window, Native.WM_APP_QUERY, 1) - beforeEvents;
                    string metrics = String.Format(CultureInfo.InvariantCulture,
                        "Engine observation: {0:F3} CPU ms over {1:F2} s; {2:F5}% of one logical processor; {3:F5}% of total CPU ({4} logical processors).\r\n" +
                        "Working set: {5:F2} MiB; private bytes: {6:F2} MiB; handles: {7}; threads: {8}; WM_MOUSEMOVE callbacks: {9}.\r\n",
                        cpuMs, sample.Elapsed.TotalSeconds, cpuMs / sample.Elapsed.TotalMilliseconds * 100,
                        cpuMs / sample.Elapsed.TotalMilliseconds * 100 / Environment.ProcessorCount, Environment.ProcessorCount,
                        process.WorkingSet64 / 1048576.0, process.PrivateMemorySize64 / 1048576.0, process.HandleCount, process.Threads.Count, inputCount);
                    Console.Write(metrics); File.WriteAllText(Path.Combine(folder, "metrics.txt"), metrics);
                    bool formsLoaded = false;
                    foreach (ProcessModule module in process.Modules)
                        if (module.ModuleName.IndexOf("System.Windows.Forms", StringComparison.OrdinalIgnoreCase) >= 0) formsLoaded = true;
                    Assert(!formsLoaded, "Background engine does not load Windows Forms.");
                    Assert(!process.HasExited && EngineMessage(window, Native.WM_APP_QUERY, 0) == 1, "Engine stays responsive after observation.");
                    File.WriteAllText(path, "enabled=not-a-boolean"); EngineMessage(window, Native.WM_APP_RELOAD, 0);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 0, "Corrupt settings detach the hook and fail safely.");
                    config.Save(path); EngineMessage(window, Native.WM_APP_RELOAD, 0);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 1, "Valid calibration repairs the engine.");
                    var missing = new Configuration { Enabled = true }; missing.Save(path);
                    EngineMessage(window, Native.WM_APP_RELOAD, 0);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 0, "Uncalibrated screens keep correction off.");
                    config.Save(path); EngineMessage(window, Native.WM_APP_RELOAD, 0);
                    Assert(EngineMessage(window, Native.WM_APP_QUERY, 0) == 1, "Calibration restores correction.");
                }
                finally
                {
                    if (window != IntPtr.Zero) Native.PostMessageW(window, Native.WM_APP_EXIT, IntPtr.Zero, IntPtr.Zero);
                    if (!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(); }
                }
                Assert(process.ExitCode == 0, "Engine exits cleanly and removes its hook.");
            }
        }
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--displays") { DescribeDisplays(); return 0; }
                GeometryChecks(); ConfigChecks(); Console.WriteLine("PASS: {0:N0} assertions.", assertions);
                if (args.Length > 0 && args[0] == "--benchmark") Benchmark();
                if (args.Length > 0 && args[0] == "--engine-smoke") { EngineSmoke(); Console.WriteLine("PASS: {0:N0} assertions including engine lifecycle checks.", assertions); }
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        }
    }
}
