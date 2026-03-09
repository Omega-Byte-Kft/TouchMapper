using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using TouchMapper.Core.Models;

namespace TouchMapper.Configurator;

public static class ScreenHelper
{
    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 1;
    private const uint ENUM_CURRENT_SETTINGS = unchecked((uint)-1);

    /// <summary>
    /// Returns the screen bounds for every active display adapter.
    /// </summary>
    public static List<Rectangle> GetAllScreenBounds()
    {
        var result = new List<Rectangle>();
        var dd = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };

        for (uint ai = 0; EnumDisplayDevices(null, ai, ref dd, 0); ai++)
        {
            // Only active adapters
            if ((dd.StateFlags & 0x00000001) == 0) // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
            {
                dd.cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>();
                continue;
            }

            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettingsEx(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref dm, 0))
            {
                result.Add(new Rectangle(
                    dm.dmPositionX, dm.dmPositionY,
                    (int)dm.dmPelsWidth, (int)dm.dmPelsHeight));
            }

            dd.cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>();
        }

        return result;
    }

    /// <summary>
    /// Returns the physical screen rectangle (in pixels) for the given monitor,
    /// or null if no match is found.
    /// </summary>
    public static Rectangle? FindScreenBounds(MonitorInfo monitor)
    {
        var dd = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
        Log($"Looking for DisplayDevicePath: [{monitor.DisplayDevicePath}]");

        for (uint ai = 0; EnumDisplayDevices(null, ai, ref dd, 0); ai++)
        {
            var adapterName = dd.DeviceName;
            Log($"  Adapter[{ai}]: {adapterName}  State=0x{dd.StateFlags:X}");

            var mdd = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };

            for (uint mi = 0; EnumDisplayDevices(adapterName, mi, ref mdd, EDD_GET_DEVICE_INTERFACE_NAME); mi++)
            {
                Log($"    Monitor[{mi}]: DeviceID=[{mdd.DeviceID}]");
                if (string.Equals(mdd.DeviceID, monitor.DisplayDevicePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Log($"    MATCH!");
                    var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
                    if (EnumDisplaySettingsEx(adapterName, ENUM_CURRENT_SETTINGS, ref dm, 0))
                    {
                        Log($"    Bounds: X={dm.dmPositionX} Y={dm.dmPositionY} W={dm.dmPelsWidth} H={dm.dmPelsHeight}");
                        return new Rectangle(
                            dm.dmPositionX, dm.dmPositionY,
                            (int)dm.dmPelsWidth, (int)dm.dmPelsHeight);
                    }
                    Log($"    EnumDisplaySettingsEx FAILED");
                }
                mdd.cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>();
            }

            dd.cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>();
        }

        Log("  NO MATCH FOUND — returning null");
        return null;
    }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TouchMapper", "overlay-debug.log");

    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { /* best-effort */ }
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice, uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(
        string lpszDeviceName, uint iModeNum,
        ref DEVMODE lpDevMode, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]  public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    // Minimal DEVMODE — only the fields we need.
    // dmPosition is a POINTL at offset 8 inside the union; we use explicit layout.
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [FieldOffset(0)]  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        [FieldOffset(64)] public ushort dmSpecVersion;
        [FieldOffset(66)] public ushort dmDriverVersion;
        [FieldOffset(68)] public ushort dmSize;
        [FieldOffset(70)] public ushort dmDriverExtra;
        [FieldOffset(72)] public uint   dmFields;
        // dmPosition (POINTL) lives at offset 76 inside the union
        [FieldOffset(76)] public int    dmPositionX;
        [FieldOffset(80)] public int    dmPositionY;
        [FieldOffset(84)] public uint   dmDisplayOrientation;
        [FieldOffset(88)] public uint   dmDisplayFixedOutput;
        // skip to PelsWidth/Height
        [FieldOffset(172)] public uint  dmPelsWidth;
        [FieldOffset(176)] public uint  dmPelsHeight;
    }
}
