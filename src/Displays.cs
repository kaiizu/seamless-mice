using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SeamlessMice
{
    public sealed class Display
    {
        public string Id, Name, Device;
        public PixelRect Pixels;
        public double WidthMm, HeightMm;
        public bool HasPhysicalSize;
        public double Diagonal { get { return Math.Sqrt(WidthMm * WidthMm + HeightMm * HeightMm) / 25.4; } }
    }
    public static class Displays
    {
        public static Display[] Enumerate()
        {
            var found = new List<Display>();
            Native.MonitorProc callback = delegate(IntPtr monitor, IntPtr dc, ref Native.RECT rect, IntPtr data)
            {
                Native.MONITORINFO info = new Native.MONITORINFO();
                info.size = Marshal.SizeOf(typeof(Native.MONITORINFO));
                if (!Native.GetMonitorInfoW(monitor, ref info)) return true;
                Native.DISPLAY_DEVICE device = new Native.DISPLAY_DEVICE();
                device.size = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
                // EDD_GET_DEVICE_INTERFACE_NAME yields the physical DISPLAY device instance,
                // rather than MONITOR\\model\\{class-guid}\\index (which is not an Enum key).
                Native.EnumDisplayDevicesW(info.device, 0, ref device, 1);
                string id = device.id;
                if (!String.IsNullOrEmpty(id) && id.StartsWith(@"\\?\", StringComparison.Ordinal))
                {
                    string[] parts = id.Split('#');
                    if (parts.Length >= 3) id = @"DISPLAY\" + parts[1] + @"\" + parts[2];
                }
                var display = new Display { Id = String.IsNullOrEmpty(id) ? info.device : id,
                    Name = String.IsNullOrEmpty(device.description) ? info.device : device.description.Trim(),
                    Device = info.device, Pixels = info.monitor.Pixels };
                ReadEdid(display);
                if (!display.HasPhysicalSize)
                {
                    // Explicitly shown as an estimate; calibration must be confirmed before activation.
                    display.WidthMm = Panel.WidthFromDiagonal(24, display.Pixels);
                    display.HeightMm = Panel.HeightFromDiagonal(24, display.Pixels);
                }
                found.Add(display);
                return true;
            };
            if (!Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot enumerate displays.");
            found.Sort(delegate(Display a, Display b) { return a.Pixels.Left.CompareTo(b.Pixels.Left); });
            return found.ToArray();
        }
        private static void ReadEdid(Display display)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + display.Id + @"\Device Parameters"))
                {
                    if (key == null) return;
                    byte[] edid = key.GetValue("EDID") as byte[];
                    if (edid == null || edid.Length < 128) return;
                    int checksum = 0;
                    for (int i = 0; i < 128; i++) checksum += edid[i];
                    if ((checksum & 255) != 0 || edid[0] != 0 || edid[1] != 255) return;
                    // The driver description is often just "Generic PnP Monitor". EDID's
                    // 0xFC descriptor contains the panel/monitor name, read once by the editor.
                    for (int offset = 54; offset <= 108; offset += 18)
                    {
                        if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 3] != 0xfc) continue;
                        string name = Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\r', '\n', ' ');
                        bool printable = name.Length > 0;
                        foreach (char character in name) if (character < 32 || character > 126) printable = false;
                        if (printable) { display.Name = name; break; }
                    }
                    double w = edid[21] * 10, h = edid[22] * 10;
                    for (int offset = 54; offset <= 108; offset += 18)
                    {
                        if (edid[offset] == 0 && edid[offset + 1] == 0) continue;
                        int detailedW = edid[offset + 12] | ((edid[offset + 14] & 0xf0) << 4);
                        int detailedH = edid[offset + 13] | ((edid[offset + 14] & 0x0f) << 8);
                        if (detailedW >= 50 && detailedH >= 50) { w = detailedW; h = detailedH; break; }
                    }
                    if ((display.Pixels.Width < display.Pixels.Height) != (w < h)) { double swap = w; w = h; h = swap; }
                    if (w < 50 || h < 50 || w > 5000 || h > 5000) return;
                    display.WidthMm = w; display.HeightMm = h; display.HasPhysicalSize = true;
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (System.IO.IOException) { }
        }
    }
}
