using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace SeamlessMice
{
    internal sealed class Engine : IDisposable
    {
        private readonly Native.WindowProc windowProc;
        private readonly Native.HookProc hookProc;
        private IntPtr window, hook;
        private Native.NOTIFYICONDATA tray;
        private Layout layout;
        private Configuration config;
        private int source = -1;
        private uint mouseEvents;
        private PixelPoint last;
        private bool warping, paused = true, userPaused, locked, disposed;
        private string status = "Open settings to calibrate your screens";
        private readonly uint taskbarCreated;
        private bool hotkeyAvailable;

        private Engine()
        {
            windowProc = WndProc; hookProc = MouseProc;
            var cls = new Native.WNDCLASS { name = Native.WindowClass, proc = windowProc,
                instance = Native.GetModuleHandleW(null) };
            if (Native.RegisterClassW(ref cls) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            // A hidden top-level window receives display, power and Explorer restart broadcasts.
            window = Native.CreateWindowExW(0, Native.WindowClass, "Seamless Mice", 0,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, cls.instance, IntPtr.Zero);
            if (window == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");
            tray = new Native.NOTIFYICONDATA { size = (uint)Marshal.SizeOf(typeof(Native.NOTIFYICONDATA)),
                hwnd = window, id = 1, flags = 1 | 2 | 4, callback = Native.WM_APP_TRAY,
                icon = Native.LoadIconW(IntPtr.Zero, new IntPtr(32516)), tip = "Seamless Mice" };
            Native.Shell_NotifyIconW(0, ref tray);
            hotkeyAvailable = Native.RegisterHotKey(window, 1, 0x4000 | 1 | 2 | 4, (uint)'M');
            Native.WTSRegisterSessionNotification(window, 0);
            Reload();
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Native.EnableDpi();
            IntPtr existing = Native.FindWindowW(Native.WindowClass, null);
            uint command = Native.WM_APP_SETTINGS;
            bool background = false;
            foreach (string arg in args)
            {
                if (arg == "--background") background = true;
                else if (arg == "--exit") command = Native.WM_APP_EXIT;
                else if (arg == "--pause") command = Native.WM_APP_PAUSE;
                else if (arg == "--resume") command = Native.WM_APP_RESUME;
                else if (arg != "--settings") return 2;
            }
            if (existing != IntPtr.Zero)
            {
                if (!background) Native.PostMessageW(existing, command, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }
            if (command != Native.WM_APP_SETTINGS) return 0;
            bool owner;
            using (var singleInstance = new Mutex(true, @"Local\SeamlessMice.Engine.v1", out owner))
            {
                if (!owner) return 0;
                try
                {
                    using (var engine = new Engine())
                    {
                        if (!background) engine.OpenSettings();
                        Native.MSG message;
                        int result;
                        while ((result = Native.GetMessageW(out message, IntPtr.Zero, 0, 0)) > 0)
                        {
                            Native.TranslateMessage(ref message); Native.DispatchMessageW(ref message);
                        }
                        return result < 0 ? 1 : 0;
                    }
                }
                catch (Exception error)
                {
                    Native.MessageBoxW(IntPtr.Zero, error.Message, "Seamless Mice could not start", 0x10);
                    return 1;
                }
            }
        }

        private void Reload()
        {
            // Never run slow discovery or file IO while the hook is installed.
            StopHook(); paused = true; layout = null;
            try
            {
                config = Configuration.Read(Configuration.DefaultPath);
                if (!config.Enabled) { status = "Paused - open settings to enable"; UpdateTray(); return; }
                layout = config.MakeLayout(Displays.Enumerate());
                if (layout.Panels.Length < 2) { status = "Waiting for a second screen"; UpdateTray(); return; }
                if (userPaused) status = "Paused - Windows cursor behavior";
                else if (!locked) StartHook();
                else status = "Paused while the session is locked";
            }
            catch (Exception error) { status = "Paused: " + error.Message; }
            UpdateTray();
        }

        private void StartHook()
        {
            StopHook(); source = -1;
            Native.POINT cursor;
            if (Native.GetCursorPos(out cursor))
            {
                last = new PixelPoint(cursor.X, cursor.Y); source = layout.Find(last);
            }
            // Compile and exercise the managed geometry path before accepting live input.
            for (int i = 0; i < layout.Panels.Length; i++)
            {
                Panel p = layout.Panels[i];
                layout.Move(i, new PixelPoint(p.Pixels.Left, p.Pixels.Top));
                layout.Move(i, new PixelPoint(p.Pixels.Right, p.Pixels.Top));
                layout.Move(i, new PixelPoint(p.Pixels.Left - 1, p.Pixels.Top));
            }
            hook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, hookProc, Native.GetModuleHandleW(null), 0);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot install the mouse hook.");
            paused = false; status = "Active - physical height correction";
        }

        private void StopHook()
        {
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            source = -1;
        }

        private unsafe IntPtr MouseProc(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || message != new IntPtr(Native.WM_MOUSEMOVE) || warping || paused || layout == null)
                return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
            // Read the blittable native struct directly: PtrToStructure would box on every event.
            Native.MOUSE* input = (Native.MOUSE*)data.ToPointer();
            mouseEvents++;
            PixelPoint point = new PixelPoint(input->pt.X, input->pt.Y);
            if ((input->flags & Native.LLMHF_INJECTED) != 0)
            {
                // Respect remote input, accessibility tools and programmatic cursor movement.
                source = -1;
                return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
            }
            if (source < 0)
            {
                source = layout.Find(point); last = point;
                return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
            }
            PixelRect bounds = layout.Panels[source].Pixels;
            PixelPoint probe = point;
            // Windows may clip the event to an edge before delivery. Recognize an outward
            // approach (or repeated push) at that pixel without polling the mouse position.
            if (point.X == bounds.Right - 1 && (last.X < point.X || (last.X == point.X && last.Y == point.Y))) probe.X++;
            else if (point.X == bounds.Left && (last.X > point.X || (last.X == point.X && last.Y == point.Y))) probe.X--;
            if (point.Y == bounds.Bottom - 1 && (last.Y < point.Y || (last.X == point.X && last.Y == point.Y))) probe.Y++;
            else if (point.Y == bounds.Top && (last.Y > point.Y || (last.X == point.X && last.Y == point.Y))) probe.Y--;
            if (bounds.Contains(probe))
            {
                last = point;
                return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
            }
            // Queries and modifier checks happen at seams only, never for ordinary movement.
            if ((Native.GetAsyncKeyState(0x10) & 0x8000) != 0 || ShouldBypass(bounds))
            {
                source = layout.Find(point); last = point;
                return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
            }
            Transition move = layout.Move(source, probe);
            if (!move.Reposition || (move.Point.X == point.X && move.Point.Y == point.Y))
            {
                last = point;
                return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
            }
            bool success;
            warping = true;
            try { success = Native.SetCursorPos(move.Point.X, move.Point.Y); }
            finally { warping = false; }
            if (!success)
            {
                source = -1;
                return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
            }
            source = move.Screen; last = move.Point;
            return new IntPtr(1);
        }

        private bool ShouldBypass(PixelRect screen)
        {
            Native.RECT clip;
            if (Native.GetClipCursor(out clip))
            {
                // Honor games' ClipCursor restrictions; never warp out of their permitted area.
                int left = Int32.MaxValue, top = Int32.MaxValue, right = Int32.MinValue, bottom = Int32.MinValue;
                foreach (Panel p in layout.Panels)
                {
                    left = Math.Min(left, p.Pixels.Left); top = Math.Min(top, p.Pixels.Top);
                    right = Math.Max(right, p.Pixels.Right); bottom = Math.Max(bottom, p.Pixels.Bottom);
                }
                if (clip.Left > left || clip.Top > top || clip.Right < right || clip.Bottom < bottom) return true;
            }
            if (!config.PauseFullscreen) return false;
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == Native.GetShellWindow() || foreground == Native.GetDesktopWindow()) return false;
            Native.RECT client;
            Native.POINT origin = new Native.POINT();
            if (!Native.GetClientRect(foreground, out client) || !Native.ClientToScreen(foreground, ref origin)) return false;
            return origin.X <= screen.Left && origin.Y <= screen.Top &&
                (long)origin.X + client.Right >= screen.Right && (long)origin.Y + client.Bottom >= screen.Bottom;
        }

        private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp)
        {
            try
            {
                if (message == taskbarCreated && taskbarCreated != 0) { Native.Shell_NotifyIconW(0, ref tray); return IntPtr.Zero; }
                switch (message)
                {
                    case Native.WM_APP_QUERY:
                        return wp == new IntPtr(1) ? new IntPtr((long)mouseEvents) : new IntPtr(paused ? (userPaused ? 2 : 0) : 1);
                    case Native.WM_APP_RELOAD: userPaused = false; Reload(); return IntPtr.Zero;
                    case Native.WM_APP_SETTINGS: OpenSettings(); return IntPtr.Zero;
                    case Native.WM_HOTKEY:
                        if (!paused) Pause(); else { userPaused = false; Reload(); }
                        return IntPtr.Zero;
                    case Native.WM_APP_PAUSE: Pause(); return IntPtr.Zero;
                    case Native.WM_APP_RESUME: userPaused = false; Reload(); return IntPtr.Zero;
                    case Native.WM_APP_EXIT: Native.DestroyWindow(hwnd); return IntPtr.Zero;
                    case Native.WM_APP_TRAY:
                        int action = lp.ToInt32() & 0xffff;
                        if (action == 0x203) OpenSettings(); // double click
                        else if (action == 0x205 || action == 0x7b) ShowMenu();
                        return IntPtr.Zero;
                    case Native.WM_DISPLAYCHANGE:
                    case Native.WM_DEVICECHANGE:
                        ScheduleReload(); return IntPtr.Zero;
                    case Native.WM_TIMER:
                        Native.KillTimer(hwnd, new UIntPtr(1)); Reload(); return IntPtr.Zero;
                    case Native.WM_POWERBROADCAST:
                        if (wp.ToInt32() == 4) { StopHook(); paused = true; status = "Paused while sleeping"; UpdateTray(); }
                        else if (wp.ToInt32() == 18 || wp.ToInt32() == 7) ScheduleReload();
                        return new IntPtr(1);
                    case Native.WM_WTSSESSION_CHANGE:
                        if (wp.ToInt32() == 7) { locked = true; StopHook(); paused = true; status = "Paused while the session is locked"; UpdateTray(); }
                        else if (wp.ToInt32() == 8) { locked = false; ScheduleReload(); }
                        return IntPtr.Zero;
                    case Native.WM_CLOSE: Native.DestroyWindow(hwnd); return IntPtr.Zero;
                    case Native.WM_DESTROY: Dispose(); Native.PostQuitMessage(0); return IntPtr.Zero;
                }
            }
            catch (Exception error)
            {
                StopHook(); paused = true; status = "Paused: " + error.Message; UpdateTray();
            }
            return Native.DefWindowProcW(hwnd, message, wp, lp);
        }
        private void ScheduleReload()
        {
            StopHook(); paused = true; status = "Waiting for display changes"; UpdateTray();
            // A debounced one-shot timer exists only during a topology/resume event.
            Native.SetTimer(window, new UIntPtr(1), 600, IntPtr.Zero);
        }
        private void Pause() { StopHook(); paused = true; userPaused = true; status = "Paused - Windows cursor behavior"; UpdateTray(); }
        private void UpdateTray()
        {
            if (window == IntPtr.Zero) return;
            string text = "Seamless Mice: " + status;
            if (!hotkeyAvailable) text += " (recovery hotkey unavailable)";
            tray.tip = text.Length > 127 ? text.Substring(0, 127) : text;
            Native.Shell_NotifyIconW(1, ref tray);
        }
        private void ShowMenu()
        {
            IntPtr menu = Native.CreatePopupMenu();
            if (menu == IntPtr.Zero) return;
            try
            {
                Native.AppendMenuW(menu, 1, UIntPtr.Zero, status);
                Native.AppendMenuW(menu, 0x800, UIntPtr.Zero, null);
                Native.AppendMenuW(menu, 0, new UIntPtr(1), "Settings...");
                Native.AppendMenuW(menu, 0, new UIntPtr(2), paused ? "Resume correction" : "Pause correction");
                Native.AppendMenuW(menu, 0, new UIntPtr(3), "Re-detect screens / repair hook");
                Native.AppendMenuW(menu, 0x800, UIntPtr.Zero, null);
                Native.AppendMenuW(menu, 0, new UIntPtr(4), "Exit");
                Native.POINT p; Native.GetCursorPos(out p);
                Native.SetForegroundWindow(window);
                int choice = Native.TrackPopupMenu(menu, 0x100 | 2, p.X, p.Y, 0, window, IntPtr.Zero);
                if (choice == 1) OpenSettings();
                else if (choice == 2) { if (paused) { userPaused = false; Reload(); } else Pause(); }
                else if (choice == 3) Reload();
                else if (choice == 4) Native.DestroyWindow(window);
                Native.PostMessageW(window, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { Native.DestroyMenu(menu); }
        }
        private void OpenSettings()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SeamlessMice.Settings.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("Settings executable is missing. Run build.ps1 again.", path);
            bool restore = !paused;
            if (restore) { StopHook(); paused = true; }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory }); }
            finally { if (restore) { StartHook(); UpdateTray(); } }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            StopHook(); Native.KillTimer(window, new UIntPtr(1));
            Native.UnregisterHotKey(window, 1); Native.WTSUnRegisterSessionNotification(window);
            Native.Shell_NotifyIconW(2, ref tray);
        }
    }
}
