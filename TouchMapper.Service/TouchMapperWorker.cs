using System.IO;
using System.Management;
using TouchMapper.Core.Config;
using TouchMapper.Core.Detection;
using TouchMapper.Core.Mapping;

namespace TouchMapper.Service;

public sealed class TouchMapperWorker : BackgroundService
{
    private readonly ILogger<TouchMapperWorker> _logger;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "TouchMapper", "service.log");

    // Debounce: wait this long after a display-change event before re-applying mapping
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    public TouchMapperWorker(ILogger<TouchMapperWorker> logger)
    {
        _logger = logger;
    }

    private static void FileLog(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n");
        }
        catch { /* best-effort */ }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TouchMapper service starting.");
        FileLog("════ TouchMapper service starting ════");

        // Apply on startup
        ApplyCorrectMapping();
        _lastTopologyFingerprint = GetTopologyFingerprint();

        // Watch for display change events via WMI
        using var watcher = CreateDisplayWatcher();
        watcher.EventArrived += (_, _) => OnDisplayChange();
        watcher.Start();

        _logger.LogInformation("Watching for display change events.");
        await Task.Delay(Timeout.Infinite, stoppingToken);

        watcher.Stop();
    }

    private ManagementEventWatcher CreateDisplayWatcher()
    {
        // Win32_DeviceChangeEvent EventType 2 = device arrived, 3 = device removed
        var query = new WqlEventQuery(
            "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3");
        return new ManagementEventWatcher(query);
    }

    private readonly object _applyLock = new();
    private CancellationTokenSource? _debounce;
    private string _lastTopologyFingerprint = "";

    private void OnDisplayChange()
    {
        // Debounce: cancel any pending check and schedule a new one after SettleDelay
        lock (_applyLock)
        {
            _debounce?.Cancel();
            _debounce = new CancellationTokenSource();
            var token = _debounce.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(SettleDelay, token);

                    // Check if topology actually changed before re-applying
                    var fingerprint = GetTopologyFingerprint();
                    if (fingerprint == _lastTopologyFingerprint)
                        return; // nothing changed — ignore this event

                    FileLog($"Topology changed (was {_lastTopologyFingerprint.Length} chars, now {fingerprint.Length} chars)");
                    _logger.LogInformation("Display/touch topology changed — applying mapping.");
                    ApplyCorrectMapping();
                    _lastTopologyFingerprint = fingerprint;
                }
                catch (OperationCanceledException) { /* superseded by newer event */ }
            }, token);
        }
    }

    private static string GetTopologyFingerprint()
    {
        try
        {
            var monitors = EdidDetector.GetActiveMonitors()
                .Select(m => m.EdidSerial + "|" + m.DisplayDevicePath)
                .OrderBy(s => s);
            var touches = TouchDetector.GetActiveTouchDevices()
                .Select(t => t.PnpInstanceId + "|" + t.UsbLocationPath)
                .OrderBy(s => s);
            return string.Join(";", monitors) + "##" + string.Join(";", touches);
        }
        catch
        {
            return ""; // on error, allow re-apply
        }
    }

    private void ApplyCorrectMapping()
    {
        try
        {
            FileLog("── ApplyCorrectMapping ──────────────────────");

            var config = ConfigStore.Load();
            if (config is null)
            {
                FileLog($"No config found at {ConfigStore.ConfigPath}");
                _logger.LogWarning("No config found at {path}. Run the Configurator first.", ConfigStore.ConfigPath);
                return;
            }

            FileLog($"Config loaded: SchemaVersion={config.SchemaVersion} Mappings={config.Mappings.Count}");
            foreach (var m in config.Mappings)
                FileLog($"  Mapping: usbId={m.UsbIdentifier} monitor={m.MonitorEdidSerial} ({m.MonitorFriendlyName})");

            var monitors = EdidDetector.GetActiveMonitors();
            var touches = TouchDetector.GetActiveTouchDevices();

            _logger.LogInformation("Found {m} monitors, {t} touch devices.", monitors.Count, touches.Count);
            FileLog($"Live monitors: {monitors.Count}");
            foreach (var m in monitors)
                FileLog($"  {m.EdidSerial} model={m.MonitorModel} internal={m.IsInternal} path={m.DisplayDevicePath}");

            FileLog($"Live touch devices: {touches.Count}");
            foreach (var t in touches)
                FileLog($"  {t.PnpInstanceId} usb={t.UsbLocationPath} hops={t.HopCount} hid={t.HidDevicePath}");

            var entries = MappingEngine.ComputeEntries(config, monitors, touches);
            FileLog($"Computed entries: {entries.Count}");
            foreach (var e in entries)
                FileLog($"  {e.HidDevicePath} → {e.DisplayDevicePath}");

            if (entries.Count == 0)
            {
                FileLog("No mappings computed — nothing to apply.");
                _logger.LogWarning("No mappings computed — monitors or touch devices may not match config.");
                return;
            }

            WispManager.ApplyMappings(entries);
            FileLog($"Applied {entries.Count} mapping(s) successfully.");
            _logger.LogInformation("Applied {n} Wisp mapping(s).", entries.Count);
        }
        catch (Exception ex)
        {
            FileLog($"ERROR: {ex}");
            _logger.LogError(ex, "Failed to apply touch mapping.");
        }
    }
}
