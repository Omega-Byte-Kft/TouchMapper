using System.IO;
using System.Runtime.InteropServices;
using WpfColor = System.Windows.Media.Color;
using GdiRect  = System.Drawing.Rectangle;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TouchMapper.Core.Mapping;
using TouchMapper.Core.Models;

namespace TouchMapper.Configurator;

public partial class TestOverlayWindow : Window
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TouchMapper", "overlay-debug.log");

    private readonly MonitorInfo _targetMonitor;
    private readonly List<WispEntry> _entries;
    private GdiRect _screenBounds;
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _secondsLeft = 20;
    private bool _resultShown;
    private IntPtr _mouseHook;
    private NativeMethods.LowLevelMouseProc? _mouseHookDelegate;
    private Dictionary<string, string>? _originalMappings;
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

    public TestOverlayWindow(MonitorInfo targetMonitor, List<WispEntry> entries)
    {
        InitializeComponent();
        _targetMonitor = targetMonitor;
        _entries = entries;

        MonitorLabel.Text = targetMonitor.ToString();
        ApplyingPanel.Visibility = Visibility.Visible;
        InstructionText.Visibility = Visibility.Collapsed;
        CountdownText.Text = string.Empty;

        WindowStartupLocation = WindowStartupLocation.Manual;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Log($"Target monitor: {_targetMonitor}");
        Log($"  DisplayDevicePath: {_targetMonitor.DisplayDevicePath}");

        var bounds = ScreenHelper.FindScreenBounds(_targetMonitor);
        Log($"FindScreenBounds returned: {(bounds.HasValue ? bounds.Value.ToString() : "NULL")}");

        _screenBounds = bounds ?? new GdiRect(
            0, 0,
            (int)SystemParameters.PrimaryScreenWidth,
            (int)SystemParameters.PrimaryScreenHeight);

        Log($"Using screenBounds: X={_screenBounds.X} Y={_screenBounds.Y} W={_screenBounds.Width} H={_screenBounds.Height}");

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            _screenBounds.Left, _screenBounds.Top,
            _screenBounds.Width, _screenBounds.Height,
            NativeMethods.SWP_SHOWWINDOW);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Show blocking overlays on all non-target monitors
        ShowBlockingOverlays();

        // Save current mappings before overwriting, then apply test mappings
        try
        {
            await Task.Run(() =>
            {
                _originalMappings = WispManager.SaveSnapshot();
                WispManager.ApplyMappings(_entries);
            });
        }
        catch { /* best-effort; still show test */ }

        // Let HID devices settle after pnputil restart
        await Task.Delay(2000);

        ApplyingPanel.Visibility = Visibility.Collapsed;
        InstructionText.Visibility = Visibility.Visible;

        Log($"OnLoaded: WPF Left={Left} Top={Top} ActualWidth={ActualWidth} ActualHeight={ActualHeight}");

        // Install a global low-level mouse hook to detect clicks on ANY display.
        // Touch-promoted mouse events are delivered system-wide, so this catches
        // both correct-display and wrong-display touches.
        _mouseHookDelegate = MouseHookCallback;
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _mouseHookDelegate, IntPtr.Zero, 0);

        Log($"Mouse hook installed: {_mouseHook != IntPtr.Zero}");

        _countdown.Tick += CountdownTick;
        _countdown.Start();
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_LBUTTONDOWN && !_resultShown)
        {
            var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            bool onTarget = info.pt.x >= _screenBounds.Left
                         && info.pt.x <  _screenBounds.Right
                         && info.pt.y >= _screenBounds.Top
                         && info.pt.y <  _screenBounds.Bottom;

            Log($"MouseHook: screen=({info.pt.x}, {info.pt.y}) " +
                $"target=[{_screenBounds.Left},{_screenBounds.Top} {_screenBounds.Right},{_screenBounds.Bottom}] " +
                $"onTarget={onTarget}");

            Dispatcher.BeginInvoke(() =>
            {
                ShowResult(onTarget,
                    onTarget
                        ? "✓  Touch received on the correct display!"
                        : "✗  Touch landed on a different display.\nCheck the binding and try again.");
            });
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void CountdownTick(object? sender, EventArgs e)
    {
        _secondsLeft--;
        if (_secondsLeft > 0)
        {
            CountdownText.Text = $"Closes in {_secondsLeft}s";
        }
        else
        {
            _countdown.Stop();
            if (!_resultShown)
                ShowResult(false, "No touch detected on this screen.");
        }
    }

    private void ShowResult(bool success, string message)
    {
        if (_resultShown) return;
        _resultShown = true;

        RemoveMouseHook();
        _countdown.Stop();

        Log($"Result: success={success} message={message}");

        Background = success
            ? new SolidColorBrush(WpfColor.FromRgb(0x1B, 0x87, 0x3A))
            : new SolidColorBrush(WpfColor.FromRgb(0xC0, 0x1A, 0x1A));

        InstructionText.Visibility = Visibility.Collapsed;
        ResultText.Text = message;
        ResultText.Visibility = Visibility.Visible;
        CountdownText.Text = "Closing in 3s…";

        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            Dispatcher.Invoke(Close);
        });
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private void ShowBlockingOverlays()
    {
        foreach (var bounds in ScreenHelper.GetAllScreenBounds())
        {
            // Skip the target monitor's screen
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
        RemoveMouseHook();
        _countdown.Stop();
        CloseBlockingOverlays();

        // Fire-and-forget restore so the window closes immediately
        if (_originalMappings is { } snapshot)
        {
            _originalMappings = null;
            Task.Run(() =>
            {
                try { WispManager.RestoreSnapshot(snapshot); }
                catch { /* best-effort */ }
            });
        }

        base.OnClosed(e);
    }

    private static class NativeMethods
    {
        public static readonly IntPtr HWND_TOPMOST = new(-1);
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const int WH_MOUSE_LL = 14;
        public const int WM_LBUTTONDOWN = 0x0201;

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);
    }
}
