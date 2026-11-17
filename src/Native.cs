using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SeamlessMice
{
    internal static class Native
    {
        internal const int WH_MOUSE_LL = 14, WM_MOUSEMOVE = 0x200, WM_DESTROY = 2, WM_CLOSE = 0x10,
            WM_COMMAND = 0x111, WM_TIMER = 0x113, WM_DISPLAYCHANGE = 0x7e, WM_DEVICECHANGE = 0x219,
            WM_POWERBROADCAST = 0x218, WM_HOTKEY = 0x312, WM_WTSSESSION_CHANGE = 0x2b1,
            WM_APP_TRAY = 0x8001, WM_APP_RELOAD = 0x8002, WM_APP_PAUSE = 0x8003,
            WM_APP_EXIT = 0x8004, WM_APP_SETTINGS = 0x8005, WM_APP_RESUME = 0x8006,
            WM_APP_QUERY = 0x8008;
        internal const string WindowClass = "SeamlessMice.Engine.v1";
        internal const uint LLMHF_INJECTED = 1;
        internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        internal delegate IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
        internal delegate bool MonitorProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT
        {
            internal int Left, Top, Right, Bottom;
            internal PixelRect Pixels { get { return new PixelRect(Left, Top, Right, Bottom); } }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct MSG
        {
            internal IntPtr hwnd; internal uint message; internal UIntPtr wParam; internal IntPtr lParam;
            internal uint time; internal POINT pt; internal uint lPrivate;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSE
        { internal POINT pt; internal uint mouseData, flags, time; internal UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WNDCLASS
        {
            internal uint style; internal WindowProc proc; internal int clsExtra, wndExtra;
            internal IntPtr instance, icon, cursor, brush;
            internal string menu, name;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MONITORINFO
        {
            internal int size; internal RECT monitor, work; internal uint flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string device;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct DISPLAY_DEVICE
        {
            internal int size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string description;
            internal uint flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string key;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct NOTIFYICONDATA
        {
            internal uint size; internal IntPtr hwnd; internal uint id, flags, callback;
            internal IntPtr icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string tip;
            internal uint state, stateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string info;
            internal uint timeout;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string title;
            internal uint infoFlags; internal Guid guid; internal IntPtr balloonIcon;
        }
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookExW(int type, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern bool GetClipCursor(out RECT r);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern ushort RegisterClassW(ref WNDCLASS wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetMessageW(out MSG message, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref MSG message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr DispatchMessageW(ref MSG message);
        [DllImport("user32.dll")] internal static extern void PostQuitMessage(int code);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowW(string cls, string title);
        [DllImport("user32.dll")] internal static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint ms, IntPtr proc);
        [DllImport("user32.dll")] internal static extern bool KillTimer(IntPtr hwnd, UIntPtr id);
        [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplayDevicesW(string device, uint index, ref DISPLAY_DEVICE info, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] internal static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string label);
        [DllImport("user32.dll")] internal static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
        [DllImport("user32.dll")] internal static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr LoadIconW(IntPtr instance, IntPtr id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessageW(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int MessageBoxW(IntPtr hwnd, string text, string title, uint type);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandleW(string name);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern bool Shell_NotifyIconW(uint action, ref NOTIFYICONDATA data);
        [DllImport("wtsapi32.dll")] internal static extern bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);
        [DllImport("wtsapi32.dll")] internal static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        internal static void EnableDpi()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
        }
        internal static void ApplyDarkTitleBar(IntPtr window, bool dark)
        {
            if (window == IntPtr.Zero) return;
            try
            {
                int value = dark ? 1 : 0;
                // 20 is current Windows 10/11; 19 covers the 1809 immersive-dark-mode build.
                if (DwmSetWindowAttribute(window, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(window, 19, ref value, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
