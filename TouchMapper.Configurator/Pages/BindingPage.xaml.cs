using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TouchMapper.Core.Models;
using TouchMapper.Core.Mapping;

namespace TouchMapper.Configurator.Pages;

public partial class BindingPage : UserControl
{
    private readonly WizardState _state;
    private readonly Action<bool> _onChanged; // enables/disables Next

    // One entry per touch device (all groups flattened + standalone)
    private readonly List<RowHandle> _rows = [];

    private sealed record RowHandle(TouchDeviceInfo Touch, bool IsAnchor, bool IsStandalone, ComboBox MonitorCombo);

    public BindingPage(WizardState state, Action<bool> onChanged)
    {
        InitializeComponent();
        _state = state;
        _onChanged = onChanged;
        Loaded += BindingPage_Loaded;
    }

    private void BindingPage_Loaded(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        BindingList.Children.Clear();

        var allMonitors = _state.Monitors;
        var hasChained = _state.LiveTouchGroups.Count > 0;
        var hasStandalone = _state.StandaloneTouchDevices.Count > 0;

        if (!hasChained && !hasStandalone)
        {
            BindingList.Children.Add(new TextBlock
            {
                Text = "No touch devices found. Go back and re-run the scan.",
                Foreground = Brushes.Red, Margin = new Thickness(0, 8, 0, 0)
            });
            return;
        }

        // --- Chained Devices section ---
        if (hasChained)
        {
            BindingList.Children.Add(new TextBlock
            {
                Text = "Chained Devices",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            for (var gi = 0; gi < _state.LiveTouchGroups.Count; gi++)
            {
                var group = _state.LiveTouchGroups[gi];

                var header = new TextBlock
                {
                    Text = $"── Group {gi + 1} " + new string('─', 60),
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    Margin = new Thickness(0, gi == 0 ? 0 : 10, 0, 4)
                };
                BindingList.Children.Add(header);

                for (var di = 0; di < group.Count; di++)
                {
                    var touch = group[di];
                    bool isAnchor = di == 0;

                    MonitorInfo? preSelected = FindPreSelectedChained(gi, di, allMonitors);
                    var combo = BuildComboBox(allMonitors, preSelected);
                    var row = new RowHandle(touch, isAnchor, IsStandalone: false, combo);
                    _rows.Add(row);

                    BindingList.Children.Add(BuildRow(row, touch, isAnchor, combo));
                }
            }
        }

        // --- Individual Devices section ---
        if (hasStandalone)
        {
            BindingList.Children.Add(new TextBlock
            {
                Text = "Individual Devices",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, hasChained ? 16 : 0, 0, 6)
            });

            for (var i = 0; i < _state.StandaloneTouchDevices.Count; i++)
            {
                var touch = _state.StandaloneTouchDevices[i];

                MonitorInfo? preSelected = FindPreSelectedStandalone(i, allMonitors);
                var combo = BuildComboBox(allMonitors, preSelected);
                var row = new RowHandle(touch, IsAnchor: false, IsStandalone: true, combo);
                _rows.Add(row);

                BindingList.Children.Add(BuildStandaloneRow(row, touch, combo));
            }
        }

        UpdateConfig();
    }

    private MonitorInfo? FindPreSelectedChained(int groupIdx, int slotIdx, List<MonitorInfo> monitors)
    {
        if (groupIdx >= _state.Config.TopologyGroups.Count) return null;
        var cfgGroup = _state.Config.TopologyGroups[groupIdx];

        string? edid = slotIdx == 0
            ? cfgGroup.AnchorEdidSerial
            : slotIdx - 1 < cfgGroup.Children.Count
                ? cfgGroup.Children[slotIdx - 1].EdidSerial
                : null;

        return edid is null ? null
            : monitors.FirstOrDefault(m =>
                string.Equals(m.EdidSerial, edid, StringComparison.OrdinalIgnoreCase));
    }

    private MonitorInfo? FindPreSelectedStandalone(int index, List<MonitorInfo> monitors)
    {
        if (index >= _state.Config.DirectMappings.Count) return null;
        var dm = _state.Config.DirectMappings[index];

        return monitors.FirstOrDefault(m =>
            string.Equals(m.EdidSerial, dm.MonitorEdidSerial, StringComparison.OrdinalIgnoreCase));
    }

    private ComboBox BuildComboBox(List<MonitorInfo> monitors, MonitorInfo? preSelected)
    {
        var combo = new ComboBox
        {
            MinWidth = 220,
            VerticalAlignment = VerticalAlignment.Center
        };
        combo.Items.Add("— unassigned —");
        foreach (var m in monitors)
            combo.Items.Add(m);

        combo.DisplayMemberPath = null; // use ToString()
        combo.SelectedItem = preSelected ?? (object)"— unassigned —";

        combo.SelectionChanged += (_, _) => UpdateConfig();
        return combo;
    }

    private UIElement BuildRow(RowHandle row, TouchDeviceInfo touch, bool isAnchor, ComboBox combo)
    {
        var border = new Border
        {
            BorderBrush = isAnchor ? Brushes.CornflowerBlue : Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = isAnchor ? "★" : "•",
            FontSize = isAnchor ? 14 : 18,
            Foreground = isAnchor ? Brushes.CornflowerBlue : Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(icon, 0);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = isAnchor
                ? $"Touch Device  (anchor, {touch.HopCount} hops)"
                : $"Touch Device  ({touch.HopCount} hops, via anchor hub)",
            FontWeight = isAnchor ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 12
        });
        info.Children.Add(BuildPathBlock(touch.UsbLocationPath));
        Grid.SetColumn(info, 1);

        var arrow = new TextBlock
        {
            Text = "→",
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
            Foreground = Brushes.Gray
        };
        Grid.SetColumn(arrow, 2);

        Grid.SetColumn(combo, 3);

        var testBtn = new Button
        {
            Content = "Test",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(12, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        testBtn.Click += (_, _) => TestBinding(row);
        Grid.SetColumn(testBtn, 4);

        grid.Children.Add(icon);
        grid.Children.Add(info);
        grid.Children.Add(arrow);
        grid.Children.Add(combo);
        grid.Children.Add(testBtn);
        border.Child = grid;
        return border;
    }

    private UIElement BuildStandaloneRow(RowHandle row, TouchDeviceInfo touch, ComboBox combo)
    {
        var border = new Border
        {
            BorderBrush = Brushes.DarkSeaGreen,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = "◆",
            FontSize = 12,
            Foreground = Brushes.DarkSeaGreen,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(icon, 0);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = $"Touch Device  (standalone, {touch.HopCount} hops)",
            FontWeight = FontWeights.Normal,
            FontSize = 12
        });
        info.Children.Add(BuildPathBlock(touch.UsbLocationPath));
        Grid.SetColumn(info, 1);

        var arrow = new TextBlock
        {
            Text = "→",
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
            Foreground = Brushes.Gray
        };
        Grid.SetColumn(arrow, 2);

        Grid.SetColumn(combo, 3);

        var testBtn = new Button
        {
            Content = "Test",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(12, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        testBtn.Click += (_, _) => TestBinding(row);
        Grid.SetColumn(testBtn, 4);

        grid.Children.Add(icon);
        grid.Children.Add(info);
        grid.Children.Add(arrow);
        grid.Children.Add(combo);
        grid.Children.Add(testBtn);
        border.Child = grid;
        return border;
    }

    /// <summary>
    /// Builds a right-aligned path TextBlock inside a clipping container,
    /// so the left (less-important) part is trimmed when space is tight.
    /// </summary>
    private static UIElement BuildPathBlock(string path)
    {
        var tb = new TextBlock
        {
            Text = path,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextWrapping = TextWrapping.NoWrap
        };

        return new Border
        {
            ClipToBounds = true,
            Child = tb
        };
    }

    private void UpdateConfig()
    {
        _state.Config = BuildConfig();
        // Allow Next even with unassigned devices (user may not want to map all)
        bool anyAssigned = _rows.Any(r => r.MonitorCombo.SelectedItem is MonitorInfo);
        _onChanged(anyAssigned);
    }

    private TouchMapperConfig BuildConfig()
    {
        var config = new TouchMapperConfig();

        // Build TopologyGroup entries from chained rows
        for (var gi = 0; gi < _state.LiveTouchGroups.Count; gi++)
        {
            var group = _state.LiveTouchGroups[gi];
            var groupRows = _rows
                .Where(r => !r.IsStandalone && group.Contains(r.Touch))
                .OrderBy(r => r.Touch.HopCount)
                .ToList();

            var anchorRow = groupRows.FirstOrDefault(r => r.MonitorCombo.SelectedItem is MonitorInfo);
            if (anchorRow is null) continue;

            var anchorMonitor = (MonitorInfo)anchorRow.MonitorCombo.SelectedItem!;
            var cfgGroup = new TopologyGroup
            {
                AnchorEdidSerial = anchorMonitor.EdidSerial,
                AnchorFriendlyName = anchorMonitor.MonitorModel
            };

            foreach (var childRow in groupRows.Skip(1))
            {
                if (childRow.MonitorCombo.SelectedItem is not MonitorInfo childMonitor) continue;
                cfgGroup.Children.Add(new MonitorProfile
                {
                    EdidSerial = childMonitor.EdidSerial,
                    FriendlyName = childMonitor.MonitorModel
                });
            }

            config.TopologyGroups.Add(cfgGroup);
        }

        // Build DirectMapping entries from standalone rows
        foreach (var row in _rows.Where(r => r.IsStandalone))
        {
            if (row.MonitorCombo.SelectedItem is not MonitorInfo monitor) continue;
            config.DirectMappings.Add(new DirectMapping
            {
                UsbLocationPath = row.Touch.UsbLocationPath,
                MonitorEdidSerial = monitor.EdidSerial,
                MonitorFriendlyName = monitor.MonitorModel
            });
        }

        return config;
    }

    private List<WispEntry> BuildWispEntries() =>
        _rows
        .Where(r => r.MonitorCombo.SelectedItem is MonitorInfo)
        .Select(r => new WispEntry
        {
            HidDevicePath    = r.Touch.HidDevicePath,
            DisplayDevicePath = ((MonitorInfo)r.MonitorCombo.SelectedItem!).DisplayDevicePath
        })
        .ToList();

    private void TestBinding(RowHandle row)
    {
        if (row.MonitorCombo.SelectedItem is not MonitorInfo monitor)
        {
            MessageBox.Show("Select a monitor for this device first.",
                "No monitor selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var entries = BuildWispEntries();
        if (entries.Count == 0) return;

        var overlay = new TestOverlayWindow(monitor, entries);
        overlay.Show();
    }

    // ── Touch Monitor (debug: shows last touched device) ──────────────

    private HwndSource? _monitorHwndSource;
    private bool _touchMonitorActive;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TouchMapper", "binding-debug.log");

    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { /* best-effort */ }
    }

    private void TouchMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (_touchMonitorActive)
        {
            StopTouchMonitor();
            return;
        }

        _touchMonitorActive = true;
        TouchMonitorButton.Content = "Stop Debug";
        TouchMonitorPanel.Visibility = Visibility.Visible;
        LastTouchDeviceText.Text = "Waiting for touch...";
        LastTouchUsbPathText.Text = "";
        LastTouchMatchText.Text = "";

        Log("TouchMonitor: starting");

        // Get the HwndSource from the parent window
        var parentWindow = Window.GetWindow(this);
        if (parentWindow is null) return;

        _monitorHwndSource = PresentationSource.FromVisual(parentWindow) as HwndSource;
        if (_monitorHwndSource is null) return;

        var hwnd = _monitorHwndSource.Handle;

        // Register for raw digitizer input
        RegisterDigitizerUsage(hwnd, 0x04); // Touch Screen
        RegisterDigitizerUsage(hwnd, 0x05); // Touch Pad

        Log($"TouchMonitor: registered raw input on hwnd=0x{hwnd:X}");

        _monitorHwndSource.AddHook(TouchMonitorWndProc);
    }

    private void StopTouchMonitor()
    {
        Log("TouchMonitor: stopping");
        _touchMonitorActive = false;
        TouchMonitorButton.Content = "Debug Touch";
        TouchMonitorPanel.Visibility = Visibility.Collapsed;

        if (_monitorHwndSource is not null)
        {
            _monitorHwndSource.RemoveHook(TouchMonitorWndProc);

            // Unregister raw input by registering with RIDEV_REMOVE
            var hwnd = _monitorHwndSource.Handle;
            UnregisterDigitizerUsage(hwnd, 0x04);
            UnregisterDigitizerUsage(hwnd, 0x05);
            _monitorHwndSource = null;
        }
    }

    private IntPtr TouchMonitorWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_INPUT = 0x00FF;
        if (msg != WM_INPUT || !_touchMonitorActive)
            return IntPtr.Zero;

        uint size = 0;
        RawInputNative.GetRawInputData(lParam, RawInputNative.RID_INPUT, IntPtr.Zero,
            ref size, (uint)Marshal.SizeOf<RawInputNative.RAWINPUTHEADER>());

        if (size == 0)
        {
            Log("TouchMonitor WM_INPUT: GetRawInputData size=0");
            return IntPtr.Zero;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var read = RawInputNative.GetRawInputData(lParam, RawInputNative.RID_INPUT, buffer,
                ref size, (uint)Marshal.SizeOf<RawInputNative.RAWINPUTHEADER>());

            if (read == unchecked((uint)-1))
            {
                Log($"TouchMonitor WM_INPUT: GetRawInputData failed, error={Marshal.GetLastWin32Error()}");
                return IntPtr.Zero;
            }

            var header = Marshal.PtrToStructure<RawInputNative.RAWINPUTHEADER>(buffer);
            Log($"TouchMonitor WM_INPUT: dwType={header.dwType} hDevice=0x{header.hDevice:X} dwSize={header.dwSize}");

            if (header.hDevice == IntPtr.Zero)
            {
                Log("TouchMonitor WM_INPUT: hDevice is null");
                return IntPtr.Zero;
            }

            var devicePath = GetRawDevicePath(header.hDevice);
            Log($"TouchMonitor WM_INPUT: devicePath={devicePath ?? "(null)"}");

            if (devicePath is null) return IntPtr.Zero;

            // Normalize raw device path for matching: \\?\HID#...#inst#{guid}
            var normalizedRaw = NormalizeHidPath(devicePath);
            Log($"TouchMonitor: normalizedRaw={normalizedRaw}");

            // Match against known touch devices by normalized path
            var matched = MatchDevice(_state.TouchDevices, normalizedRaw);

            Dispatcher.BeginInvoke(() =>
            {
                LastTouchDeviceText.Text = devicePath;
                if (matched is not null)
                {
                    LastTouchUsbPathText.Text = $"USB: {matched.UsbLocationPath}";
                    LastTouchMatchText.Text = $"Matched: {matched.PnpInstanceId}";
                    LastTouchMatchText.Foreground = Brushes.Green;
                    Log($"TouchMonitor: MATCHED → {matched.PnpInstanceId} USB={matched.UsbLocationPath}");
                }
                else
                {
                    LastTouchUsbPathText.Text = "";
                    LastTouchMatchText.Text = $"No match in known devices";
                    LastTouchMatchText.Foreground = Brushes.OrangeRed;
                    Log($"TouchMonitor: NO MATCH");
                    foreach (var d in _state.TouchDevices)
                        Log($"  Known: normalized={NormalizeHidPath(d.HidDevicePath)} raw={d.HidDevicePath}");
                }
            });
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Normalizes a HID device path by stripping \\?\ prefix and trailing #{guid},
    /// then replacing # with \ to get a PnP-style instance ID.
    /// "\\?\HID#VID_1FD2&amp;PID_8103&amp;MI_00#a&amp;1234#{guid}" → "HID\VID_1FD2&amp;PID_8103&amp;MI_00\a&amp;1234"
    /// </summary>
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

    /// <summary>
    /// Matches a raw input device path against known TouchDeviceInfo list.
    /// Uses full normalized instance ID for exact matching (not just VID/PID).
    /// </summary>
    private static TouchDeviceInfo? MatchDevice(List<TouchDeviceInfo> devices, string normalizedRaw)
    {
        // Try exact match on full normalized path first
        var exact = devices.FirstOrDefault(d =>
            string.Equals(NormalizeHidPath(d.HidDevicePath), normalizedRaw,
                StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Fallback: check if one contains the other (different path formats)
        return devices.FirstOrDefault(d =>
            normalizedRaw.Contains(NormalizeHidPath(d.HidDevicePath), StringComparison.OrdinalIgnoreCase)
            || NormalizeHidPath(d.HidDevicePath).Contains(normalizedRaw, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetRawDevicePath(IntPtr hDevice)
    {
        uint size = 0;
        RawInputNative.GetRawInputDeviceInfo(hDevice, RawInputNative.RIDI_DEVICENAME,
            IntPtr.Zero, ref size);

        if (size == 0) return null;

        var buffer = Marshal.AllocHGlobal((int)(size * 2));
        try
        {
            var result = RawInputNative.GetRawInputDeviceInfo(hDevice,
                RawInputNative.RIDI_DEVICENAME, buffer, ref size);
            return result < 0 ? null : Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void RegisterDigitizerUsage(IntPtr hwnd, ushort usage)
    {
        var rid = new RawInputNative.RAWINPUTDEVICE
        {
            usUsagePage = 0x0D,
            usUsage = usage,
            dwFlags = RawInputNative.RIDEV_INPUTSINK,
            hwndTarget = hwnd
        };
        var ok = RawInputNative.RegisterRawInputDevices([rid], 1,
            (uint)Marshal.SizeOf<RawInputNative.RAWINPUTDEVICE>());
        Log($"RegisterDigitizerUsage(0x{usage:X2}): {ok}, error={Marshal.GetLastWin32Error()}");
    }

    private static void UnregisterDigitizerUsage(IntPtr hwnd, ushort usage)
    {
        var rid = new RawInputNative.RAWINPUTDEVICE
        {
            usUsagePage = 0x0D,
            usUsage = usage,
            dwFlags = RawInputNative.RIDEV_REMOVE,
            hwndTarget = IntPtr.Zero
        };
        RawInputNative.RegisterRawInputDevices([rid], 1,
            (uint)Marshal.SizeOf<RawInputNative.RAWINPUTDEVICE>());
    }

    private static class RawInputNative
    {
        public const uint RID_INPUT = 0x10000003;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIDEV_REMOVE = 0x00000001;
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

    // ── Auto-Detect Walkthrough ────────────────────────────────────────

    private void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Monitors.Count == 0 || _state.TouchDevices.Count == 0)
        {
            MessageBox.Show("No monitors or touch devices detected.",
                "Nothing to detect", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AutoDetectButton.IsEnabled = false;

        var monitors = _state.Monitors.ToList();
        var remaining = _state.TouchDevices.ToList();
        var results = new Dictionary<MonitorInfo, TouchDeviceInfo>();

        ShowNextAutoDetect(0, monitors, remaining, results);
    }

    private void ShowNextAutoDetect(
        int index,
        List<MonitorInfo> monitors,
        List<TouchDeviceInfo> remaining,
        Dictionary<MonitorInfo, TouchDeviceInfo> results)
    {
        if (index >= monitors.Count || remaining.Count == 0)
        {
            FinishAutoDetect(results);
            return;
        }

        var monitor = monitors[index];
        var win = new AutoDetectWindow(monitor, remaining, device =>
        {
            if (device is not null)
            {
                results[monitor] = device;
                remaining.Remove(device);
            }
        });

        win.Closed += (_, _) =>
        {
            if (win.Cancelled)
            {
                FinishAutoDetect(results);
                return;
            }

            ShowNextAutoDetect(index + 1, monitors, remaining, results);
        };

        win.Show();
    }

    private void FinishAutoDetect(
        Dictionary<MonitorInfo, TouchDeviceInfo> results)
    {
        // Apply results to ComboBoxes
        foreach (var (monitor, touch) in results)
        {
            var row = _rows.FirstOrDefault(r => r.Touch == touch);
            if (row is null) continue;

            // Find the monitor in the ComboBox items and select it
            foreach (var item in row.MonitorCombo.Items)
            {
                if (item is MonitorInfo m &&
                    string.Equals(m.EdidSerial, monitor.EdidSerial, StringComparison.OrdinalIgnoreCase))
                {
                    row.MonitorCombo.SelectedItem = m;
                    break;
                }
            }
        }

        UpdateConfig();
        AutoDetectButton.IsEnabled = true;
    }

}
