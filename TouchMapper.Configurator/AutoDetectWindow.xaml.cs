using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using GdiRect = System.Drawing.Rectangle;
using TouchMapper.Core.Models;

namespace TouchMapper.Configurator;

public partial class AutoDetectWindow : Window
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TouchMapper", "autodetect-debug.log");

    private readonly MonitorInfo _targetMonitor;
    private readonly List<TouchDeviceInfo> _remainingDevices;
    private readonly Action<TouchDeviceInfo?> _onResult;

    private GdiRect _screenBounds;
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _secondsLeft = 30;
    private bool _resultFired;
    private HwndSource? _hwndSource;
    private readonly List<BlockingOverlayWindow> _blockingOverlays = [];

    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { /* best-effort */ }
    }

    public AutoDetectWindow(
        MonitorInfo targetMonitor,
        List<TouchDeviceInfo> remainingDevices,
        Action<TouchDeviceInfo?> onResult)
    {
        InitializeComponent();
        _targetMonitor = targetMonitor;
        _remainingDevices = remainingDevices;
        _onResult = onResult;

        MonitorLabel.Text = targetMonitor.ToString();
        Log($"=== AutoDetect opened for monitor: {targetMonitor}");
        Log($"  Remaining devices: {remainingDevices.Count}");
        foreach (var d in remainingDevices)
            Log($"    {d.PnpInstanceId} → normalized={NormalizeHidPath(d.HidDevicePath)}");

        WindowStartupLocation = WindowStartupLocation.Manual;
        KeyDown += OnKeyDown;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            Log("User pressed Space → skip");
            FireResult(null);
        }
        else if (e.Key == Key.Escape)
        {
            Log("User pressed Escape → cancel all");
            FireResult(null, cancelAll: true);
        }
    }

    public bool Cancelled { get; private set; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var bounds = ScreenHelper.FindScreenBounds(_targetMonitor);
        Log($"FindScreenBounds: {(bounds.HasValue ? bounds.Value.ToString() : "NULL")}");

        _screenBounds = bounds ?? new GdiRect(
            0, 0,
            (int)SystemParameters.PrimaryScreenWidth,
            (int)SystemParameters.PrimaryScreenHeight);

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            _screenBounds.Left, _screenBounds.Top,
            _screenBounds.Width, _screenBounds.Height,
            NativeMethods.SWP_SHOWWINDOW);

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
        Log($"WndProc hook installed on hwnd=0x{hwnd:X}");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Show blocking overlays on all non-target monitors
        ShowBlockingOverlays();

        var hwnd = new WindowInteropHelper(this).Handle;

        RegisterDigitizerUsage(hwnd, 0x04); // Touch Screen
        RegisterDigitizerUsage(hwnd, 0x05); // Touch Pad

        _countdown.Tick += CountdownTick;
        _countdown.Start();
        Log("AutoDetect loaded, countdown started");
    }

    private static void RegisterDigitizerUsage(IntPtr hwnd, ushort usage)
    {
        var rid = new NativeMethods.RAWINPUTDEVICE
        {
            usUsagePage = 0x0D,
            usUsage = usage,
            dwFlags = NativeMethods.RIDEV_INPUTSINK,
            hwndTarget = hwnd
        };
        var ok = NativeMethods.RegisterRawInputDevices([rid], 1,
            (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
        Log($"RegisterDigitizerUsage(page=0x0D, usage=0x{usage:X2}): ok={ok}, err={Marshal.GetLastWin32Error()}");
    }

    // ── WM_INPUT: identify which HID device fired ──────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_INPUT || _resultFired)
            return IntPtr.Zero;

        uint size = 0;
        NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, IntPtr.Zero,
            ref size, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>());

        if (size == 0)
        {
            Log("WM_INPUT: GetRawInputData returned size=0");
            return IntPtr.Zero;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var read = NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, buffer,
                ref size, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>());

            if (read == unchecked((uint)-1))
            {
                Log($"WM_INPUT: GetRawInputData failed, error={Marshal.GetLastWin32Error()}");
                return IntPtr.Zero;
            }

            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
            Log($"WM_INPUT: dwType={header.dwType} hDevice=0x{header.hDevice:X} dwSize={header.dwSize}");

            if (header.hDevice == IntPtr.Zero)
            {
                Log("WM_INPUT: hDevice is null, skipping");
                return IntPtr.Zero;
            }

            var devicePath = GetDevicePath(header.hDevice);
            Log($"WM_INPUT: devicePath={devicePath ?? "(null)"}");

            if (devicePath is null) return IntPtr.Zero;

            var normalizedRaw = NormalizeHidPath(devicePath);
            Log($"WM_INPUT: normalizedRaw={normalizedRaw}");

            // Match against remaining touch devices by full normalized instance ID
            var matched = MatchDevice(_remainingDevices, normalizedRaw);

            if (matched is not null)
            {
                Log($"WM_INPUT: MATCHED → {matched.PnpInstanceId}");
                handled = true;
                Dispatcher.BeginInvoke(() => FireResult(matched));
            }
            else
            {
                Log($"WM_INPUT: no match among remaining devices");
                foreach (var d in _remainingDevices)
                    Log($"  candidate: normalized={NormalizeHidPath(d.HidDevicePath)} raw={d.HidDevicePath}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return IntPtr.Zero;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string NormalizeHidPath(string hidPath)
    {
        var s = hidPath;
        if (s.StartsWith(@"\\?\", StringComparison.Ordinal))
            s = s[4..];

        var guidStart = s.LastIndexOf("#{", StringComparison.Ordinal);
        if (guidStart >= 0)
            s = s[..guidStart];

        return s.Replace('#', '\\');
    }

    private static TouchDeviceInfo? MatchDevice(List<TouchDeviceInfo> devices, string normalizedRaw)
    {
        var exact = devices.FirstOrDefault(d =>
            string.Equals(NormalizeHidPath(d.HidDevicePath), normalizedRaw,
                StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        return devices.FirstOrDefault(d =>
            normalizedRaw.Contains(NormalizeHidPath(d.HidDevicePath), StringComparison.OrdinalIgnoreCase)
            || NormalizeHidPath(d.HidDevicePath).Contains(normalizedRaw, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetDevicePath(IntPtr hDevice)
    {
        uint size = 0;
        NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME,
            IntPtr.Zero, ref size);

        if (size == 0)
        {
            Log($"GetDevicePath: size=0 for hDevice=0x{hDevice:X}");
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)(size * 2));
        try
        {
            var result = NativeMethods.GetRawInputDeviceInfo(hDevice,
                NativeMethods.RIDI_DEVICENAME, buffer, ref size);
            if (result < 0)
            {
                Log($"GetDevicePath: GetRawInputDeviceInfo failed, result={result}, err={Marshal.GetLastWin32Error()}");
                return null;
            }
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void FireResult(TouchDeviceInfo? device, bool cancelAll = false)
    {
        if (_resultFired) return;
        _resultFired = true;
        Cancelled = cancelAll;

        Log($"FireResult: device={device?.PnpInstanceId ?? "(null)"} cancelled={cancelAll}");

        _countdown.Stop();
        _hwndSource?.RemoveHook(WndProc);

        _onResult(device);
        Close();
    }

    private void CountdownTick(object? sender, EventArgs e)
    {
        _secondsLeft--;
        if (_secondsLeft > 0)
            CountdownText.Text = $"Auto-skip in {_secondsLeft}s";
        else
        {
            Log("Countdown expired → auto-skip");
            FireResult(null);
        }
    }

    private void ShowBlockingOverlays()
    {
        foreach (var bounds in ScreenHelper.GetAllScreenBounds())
        {
            if (bounds.IntersectsWith(_screenBounds)
                && bounds.Width == _screenBounds.Width
                && bounds.Height == _screenBounds.Height)
                continue;

            var overlay = new BlockingOverlayWindow(bounds);
            _blockingOverlays.Add(overlay);
            overlay.Show();
        }
    }

    private void CloseBlockingOverlays()
    {
        foreach (var overlay in _blockingOverlays)
            overlay.Close();
        _blockingOverlays.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        _countdown.Stop();
        _hwndSource?.RemoveHook(WndProc);
        CloseBlockingOverlays();
        Log("AutoDetect window closed");
        base.OnClosed(e);
    }

    // ── P/Invoke ────────────────────────────────────────────────────────

    private static class NativeMethods
    {
        public static readonly IntPtr HWND_TOPMOST = new(-1);
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const int WM_INPUT = 0x00FF;
        public const uint RID_INPUT = 0x10000003;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIDI_DEVICENAME = 0x20000007;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(
            RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(
            IntPtr hRawInput, uint uiCommand, IntPtr pData,
            ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfo(
            IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);
    }
}
