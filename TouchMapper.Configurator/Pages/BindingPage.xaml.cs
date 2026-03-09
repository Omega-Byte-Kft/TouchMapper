using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TouchMapper.Core.Detection;
using TouchMapper.Core.Models;
using TouchMapper.Core.Mapping;

namespace TouchMapper.Configurator.Pages;

public partial class BindingPage : UserControl
{
    private readonly WizardState _state;
    private readonly Action<bool> _onChanged; // enables/disables Next

    // One entry per touch device (all groups flattened + standalone)
    private readonly List<RowHandle> _rows = [];
    private Dictionary<TouchDeviceInfo, string> _identifiers = new();

    private sealed record RowHandle(TouchDeviceInfo Touch, ComboBox MonitorCombo);

    public BindingPage(WizardState state, Action<bool> onChanged)
    {
        InitializeComponent();
        _state = state;
        _onChanged = onChanged;
        Loaded += BindingPage_Loaded;
    }

    private async void BindingPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_state.TouchDevices.Count == 0)
            await RunScanAsync();

        RebuildUI();
    }

    private async Task RunScanAsync()
    {
        _rows.Clear();
        BindingList.Children.Clear();

        var scanning = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var progress = new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 0, 0, 8) };
        var statusText = new TextBlock { Text = "Scanning for monitors and touch devices...", FontSize = 12 };
        scanning.Children.Add(progress);
        scanning.Children.Add(statusText);
        BindingList.Children.Add(scanning);

        RescanButton.IsEnabled = false;
        AutoDetectButton.IsEnabled = false;

        try
        {
            statusText.Text = "Querying WMI for active monitors...";
            var monitors = await Task.Run(() => EdidDetector.GetActiveMonitors());
            _state.Monitors = monitors;

            statusText.Text = "Querying PnP for touch devices...";
            var touches = await Task.Run(() => TouchDetector.GetActiveTouchDevices());
            _state.TouchDevices = touches;

            statusText.Text = "Analysing USB topology...";
            var allGroups = await Task.Run(() => TopologyAnalyzer.GroupByAncestor(touches));

            var chainedGroups = allGroups.Where(g => g.Count >= 2).ToList();
            var standaloneDevices = allGroups
                .Where(g => g.Count == 1)
                .Select(g => g[0])
                .ToList();

            var groupedDevices = allGroups.SelectMany(g => g).ToHashSet();
            foreach (var t in touches)
            {
                if (!groupedDevices.Contains(t))
                    standaloneDevices.Add(t);
            }

            _state.LiveTouchGroups = chainedGroups;
            _state.StandaloneTouchDevices = standaloneDevices;

            BuildDefaultConfig(monitors, chainedGroups, standaloneDevices);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Scan failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RescanButton.IsEnabled = true;
            AutoDetectButton.IsEnabled = true;
        }
    }

    private void BuildDefaultConfig(
        List<MonitorInfo> monitors,
        List<List<TouchDeviceInfo>> chainedGroups,
        List<TouchDeviceInfo> standaloneDevices)
    {
        var config = new TouchMapperConfig();
        var externalMonitors = monitors.Where(m => !m.IsInternal).ToList();
        int monitorIdx = 0;

        var allDevices = chainedGroups.SelectMany(g => g).Concat(standaloneDevices).ToList();
        var identifiers = TopologyAnalyzer.ComputeIdentifiers(allDevices);

        // Assign external monitors to chained groups first (in hop order)
        foreach (var group in chainedGroups)
        {
            foreach (var device in group)
            {
                if (monitorIdx >= externalMonitors.Count) break;
                if (!identifiers.TryGetValue(device, out var id)) continue;
                config.Mappings.Add(new TouchMapping
                {
                    UsbIdentifier = id,
                    MonitorEdidSerial = externalMonitors[monitorIdx].EdidSerial,
                    MonitorFriendlyName = externalMonitors[monitorIdx].DisplayLabel
                });
                monitorIdx++;
            }
        }

        // Remaining monitors to standalone devices
        var remainingMonitors = monitors
            .Where(m => !externalMonitors.Take(monitorIdx).Contains(m))
            .ToList();

        for (int i = 0; i < standaloneDevices.Count; i++)
        {
            if (i >= remainingMonitors.Count) break;
            if (!identifiers.TryGetValue(standaloneDevices[i], out var id)) continue;
            config.Mappings.Add(new TouchMapping
            {
                UsbIdentifier = id,
                MonitorEdidSerial = remainingMonitors[i].EdidSerial,
                MonitorFriendlyName = remainingMonitors[i].DisplayLabel
            });
        }

        _state.Config = config;
    }

    private void RebuildUI()
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
                Text = "No touch devices found. Use Re-scan Topology to retry.",
                Foreground = Brushes.Red, Margin = new Thickness(0, 8, 0, 0)
            });
            return;
        }

        // Compute identifiers for all devices
        var allDevices = _state.LiveTouchGroups.SelectMany(g => g)
            .Concat(_state.StandaloneTouchDevices).ToList();
        _identifiers = TopologyAnalyzer.ComputeIdentifiers(allDevices);

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
                BindingList.Children.Add(BuildGroupTreePanel(group, gi, allMonitors));
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

            foreach (var touch in _state.StandaloneTouchDevices)
            {
                MonitorInfo? preSelected = FindPreSelected(touch, allMonitors);
                var combo = BuildComboBox(allMonitors, preSelected);
                var row = new RowHandle(touch, combo);
                _rows.Add(row);

                BindingList.Children.Add(BuildStandaloneRow(row, touch, combo));
            }
        }

        UpdateConfig();
    }

    private MonitorInfo? FindPreSelected(TouchDeviceInfo touch, List<MonitorInfo> monitors)
    {
        if (!_identifiers.TryGetValue(touch, out var identifier)) return null;

        var mapping = _state.Config.Mappings.FirstOrDefault(m =>
            string.Equals(m.UsbIdentifier, identifier, StringComparison.OrdinalIgnoreCase));

        if (mapping is null) return null;

        return monitors.FirstOrDefault(m =>
            string.Equals(m.EdidSerial, mapping.MonitorEdidSerial, StringComparison.OrdinalIgnoreCase));
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

    private UIElement BuildGroupTreePanel(List<TouchDeviceInfo> group, int groupIndex, List<MonitorInfo> allMonitors)
    {
        var outerBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x95, 0xED)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, groupIndex == 0 ? 0 : 10, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFF))
        };

        var mainStack = new StackPanel();

        mainStack.Children.Add(new TextBlock
        {
            Text = $"Group {groupIndex + 1}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Parse paths and find common prefix (the identifier)
        var allSegments = group.Select(d => d.UsbLocationPath.Split('#')).ToList();
        int commonLen = TopologyAnalyzer.CommonPrefixLength(allSegments);
        var commonSegs = allSegments[0].Take(commonLen).ToArray();

        // Common chain visualization (dim — not the identifier)
        mainStack.Children.Add(new TextBlock
        {
            Text = "Common path:",
            FontSize = 10,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 4)
        });
        mainStack.Children.Add(BuildSegmentChain(commonSegs, highlight: false));

        // Device branches — diverge from the last common node
        for (int di = 0; di < group.Count; di++)
        {
            var touch = group[di];
            bool isAnchor = di == 0;
            bool isLast = di == group.Count - 1;
            var uniqueSegs = allSegments[di].Skip(commonLen).ToArray();

            MonitorInfo? preSelected = FindPreSelected(touch, allMonitors);
            var combo = BuildComboBox(allMonitors, preSelected);
            var row = new RowHandle(touch, combo);
            _rows.Add(row);

            mainStack.Children.Add(BuildBranchRow(row, touch, isAnchor, isLast, uniqueSegs, combo));
        }

        outerBorder.Child = mainStack;
        return outerBorder;
    }

    private UIElement BuildBranchRow(RowHandle row, TouchDeviceInfo touch, bool isAnchor, bool isLast, string[] uniqueSegments, ComboBox combo)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // tree connector
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // unique segments
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // label
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // arrow
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // combo
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // test

        var connector = new TextBlock
        {
            Text = isLast ? " └─ " : " ├─ ",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x95, 0xED)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        Grid.SetColumn(connector, 0);

        var uniqueChain = BuildSegmentChain(uniqueSegments, highlight: true);
        uniqueChain.VerticalAlignment = VerticalAlignment.Center;
        uniqueChain.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(uniqueChain, 1);

        var icon = new TextBlock
        {
            Text = isAnchor ? "★" : "•",
            FontSize = isAnchor ? 14 : 18,
            Foreground = isAnchor ? Brushes.CornflowerBlue : Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 4, 0)
        };
        Grid.SetColumn(icon, 2);

        var label = new TextBlock
        {
            Text = isAnchor
                ? $"anchor ({touch.HopCount} hops)"
                : $"child ({touch.HopCount} hops)",
            FontWeight = isAnchor ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(label, 3);

        var arrow = new TextBlock
        {
            Text = "→",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 6, 0),
            Foreground = Brushes.Gray
        };
        Grid.SetColumn(arrow, 4);

        combo.MinWidth = 180;
        combo.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(combo, 5);

        var testBtn = new Button
        {
            Content = "Test",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 3, 10, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        testBtn.Click += (_, _) => TestBinding(row);
        Grid.SetColumn(testBtn, 6);

        grid.Children.Add(connector);
        grid.Children.Add(uniqueChain);
        grid.Children.Add(icon);
        grid.Children.Add(label);
        grid.Children.Add(arrow);
        grid.Children.Add(combo);
        grid.Children.Add(testBtn);
        return grid;
    }

    private UIElement BuildStandaloneRow(RowHandle row, TouchDeviceInfo touch, ComboBox combo)
    {
        var border = new Border
        {
            BorderBrush = Brushes.DarkSeaGreen,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFF, 0xFA))
        };

        var stack = new StackPanel();

        // USB chain visualization (full path, highlighted — this is the stored identifier)
        stack.Children.Add(new TextBlock
        {
            Text = "USB path identifier:",
            FontSize = 10,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 3)
        });
        var segments = touch.UsbLocationPath.Split('#');
        stack.Children.Add(BuildSegmentChain(segments, highlight: true));

        // Device row: icon + label + arrow + combo + test
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // label
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // arrow
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // combo
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // test

        var icon = new TextBlock
        {
            Text = "◆",
            FontSize = 12,
            Foreground = Brushes.DarkSeaGreen,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(icon, 0);

        var label = new TextBlock
        {
            Text = $"Touch Device  (standalone, {touch.HopCount} hops)",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);

        var arrow = new TextBlock
        {
            Text = "→",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Foreground = Brushes.Gray
        };
        Grid.SetColumn(arrow, 2);

        combo.MinWidth = 180;
        combo.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(combo, 3);

        var testBtn = new Button
        {
            Content = "Test",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 3, 10, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        testBtn.Click += (_, _) => TestBinding(row);
        Grid.SetColumn(testBtn, 4);

        grid.Children.Add(icon);
        grid.Children.Add(label);
        grid.Children.Add(arrow);
        grid.Children.Add(combo);
        grid.Children.Add(testBtn);

        stack.Children.Add(grid);
        border.Child = stack;
        return border;
    }


    /// <summary>
    /// Formats a path segment for display. The segment name becomes the node type,
    /// and the parenthetical shows contextual info (port number, bus, interface, etc.).
    /// "USB(2)" → "USB (port 2)", "PCI(1400)" → "PCI (0x1400)", etc.
    /// </summary>
    private static string FormatSegmentLabel(string segment)
    {
        int parenStart = segment.IndexOf('(');
        if (parenStart < 0 || !segment.EndsWith(')'))
            return segment;

        string type = segment[..parenStart];
        string value = segment[(parenStart + 1)..^1];

        return type.ToUpperInvariant() switch
        {
            "PCIROOT" => $"PCIROOT (bus {value})",
            "PCI"     => $"PCI (0x{value})",
            "USBROOT" => $"USBROOT (hub {value})",
            "USB"     => $"USB (port {value})",
            "USBMI"   => $"USBMI (interface {value})",
            _         => segment
        };
    }

    /// <summary>
    /// Builds a visual chain of segment "pills" connected by arrows.
    /// When highlight is true, segments are shown in blue (these are the stored identifier).
    /// When false, segments are shown in gray (common prefix, not stored).
    /// </summary>
    private static FrameworkElement BuildSegmentChain(string[] segments, bool highlight)
    {
        var panel = new WrapPanel();

        for (int i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = " → ",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas")
                });
            }

            Brush bg, border, fg;
            if (highlight)
            {
                bg     = new SolidColorBrush(Color.FromRgb(0xDB, 0xE8, 0xFF));
                border = new SolidColorBrush(Color.FromRgb(0x64, 0x95, 0xED));
                fg     = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x8A));
            }
            else
            {
                bg     = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
                border = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                fg     = Brushes.DimGray;
            }

            var pill = new Border
            {
                Background = bg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(0, 1, 0, 1),
                Child = new TextBlock
                {
                    Text = FormatSegmentLabel(segments[i]),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    Foreground = fg,
                    FontWeight = highlight ? FontWeights.SemiBold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            panel.Children.Add(pill);
        }

        return panel;
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

        foreach (var row in _rows)
        {
            if (row.MonitorCombo.SelectedItem is not MonitorInfo monitor) continue;
            if (!_identifiers.TryGetValue(row.Touch, out var identifier)) continue;

            config.Mappings.Add(new TouchMapping
            {
                UsbIdentifier = identifier,
                MonitorEdidSerial = monitor.EdidSerial,
                MonitorFriendlyName = monitor.DisplayLabel
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

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        RescanButton.Content = "Scanning...";
        _state.TouchDevices.Clear(); // force re-scan
        await RunScanAsync();
        RebuildUI();
        RescanButton.Content = "Re-scan Topology";
    }

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
