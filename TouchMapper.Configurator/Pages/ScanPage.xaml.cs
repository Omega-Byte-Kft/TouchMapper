using System.Windows;
using System.Windows.Controls;
using TouchMapper.Core.Detection;
using TouchMapper.Core.Mapping;
using TouchMapper.Core.Models;

namespace TouchMapper.Configurator.Pages;

public partial class ScanPage : UserControl
{
    private readonly WizardState _state;
    private readonly Action<bool> _onComplete;

    public ScanPage(WizardState state, Action<bool> onComplete)
    {
        InitializeComponent();
        _state = state;
        _onComplete = onComplete;
        Loaded += ScanPage_Loaded;
    }

    private async void ScanPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        try
        {
            StatusText.Text = "Querying WMI for active monitors...";
            var monitors = await Task.Run(() => EdidDetector.GetActiveMonitors());
            _state.Monitors = monitors;

            StatusText.Text = "Querying PnP for touch devices...";
            var touches = await Task.Run(() => TouchDetector.GetActiveTouchDevices());
            _state.TouchDevices = touches;

            StatusText.Text = "Analysing USB topology...";
            var allGroups = await Task.Run(() => TopologyAnalyzer.GroupByAncestor(touches));

            // Classify: groups with 2+ devices are chained; groups with 1 are standalone
            var chainedGroups = allGroups.Where(g => g.Count >= 2).ToList();
            var standaloneDevices = allGroups
                .Where(g => g.Count == 1)
                .Select(g => g[0])
                .ToList();

            // Devices not grouped at all (no shared ancestor) are also standalone
            var groupedDevices = allGroups.SelectMany(g => g).ToHashSet();
            foreach (var t in touches)
            {
                if (!groupedDevices.Contains(t))
                    standaloneDevices.Add(t);
            }

            _state.LiveTouchGroups = chainedGroups;
            _state.StandaloneTouchDevices = standaloneDevices;

            // Pre-populate a default config from what we found
            BuildDefaultConfig(monitors, chainedGroups, standaloneDevices);

            Progress.IsIndeterminate = false;
            Progress.Value = 100;
            StatusText.Text = "Scan complete.";

            MonitorCount.Text = $"Monitors found: {monitors.Count} " +
                $"({monitors.Count(m => m.IsInternal)} built-in)";
            TouchCount.Text   = $"Touch devices found: {touches.Count}";
            GroupCount.Text   = $"Chained groups: {chainedGroups.Count}, Standalone: {standaloneDevices.Count}";

            ResultPanel.Visibility = Visibility.Visible;
            _onComplete(monitors.Count > 0 && touches.Count > 0);
        }
        catch (Exception ex)
        {
            Progress.IsIndeterminate = false;
            StatusText.Text = "Scan failed.";
            ErrorText.Text = ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
            _onComplete(false);
        }
    }

    private void BuildDefaultConfig(
        List<MonitorInfo> monitors,
        List<List<TouchDeviceInfo>> chainedGroups,
        List<TouchDeviceInfo> standaloneDevices)
    {
        var config = new TouchMapperConfig();

        // For chained groups, assign external monitors in hop order
        var externalMonitors = monitors.Where(m => !m.IsInternal).ToList();
        var usedMonitorCount = 0;

        for (var gi = 0; gi < chainedGroups.Count; gi++)
        {
            var group = chainedGroups[gi];
            if (group.Count == 0) continue;

            var monitorSlice = externalMonitors.Skip(usedMonitorCount).Take(group.Count).ToList();
            if (monitorSlice.Count == 0) continue;
            usedMonitorCount += monitorSlice.Count;

            var cfgGroup = new TopologyGroup
            {
                AnchorEdidSerial = monitorSlice[0].EdidSerial,
                AnchorFriendlyName = monitorSlice[0].MonitorModel
            };

            for (var i = 1; i < monitorSlice.Count; i++)
            {
                cfgGroup.Children.Add(new MonitorProfile
                {
                    EdidSerial = monitorSlice[i].EdidSerial,
                    FriendlyName = monitorSlice[i].MonitorModel
                });
            }

            config.TopologyGroups.Add(cfgGroup);
        }

        // For standalone devices, create DirectMappings
        // Try to pair with remaining monitors (including internal)
        var remainingMonitors = monitors
            .Where(m => !externalMonitors.Take(usedMonitorCount).Contains(m))
            .ToList();

        for (var i = 0; i < standaloneDevices.Count; i++)
        {
            var touch = standaloneDevices[i];
            var monitor = i < remainingMonitors.Count ? remainingMonitors[i] : null;

            if (monitor is not null)
            {
                config.DirectMappings.Add(new DirectMapping
                {
                    UsbLocationPath = touch.UsbLocationPath,
                    MonitorEdidSerial = monitor.EdidSerial,
                    MonitorFriendlyName = monitor.MonitorModel
                });
            }
        }

        _state.Config = config;
    }
}
