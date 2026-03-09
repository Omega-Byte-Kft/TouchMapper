using TouchMapper.Core.Models;

namespace TouchMapper.Core.Mapping;

public static class MappingEngine
{
    /// <summary>
    /// Computes Wisp entries by matching config USB identifiers to live touch devices.
    /// Matches longest (most specific) identifiers first to avoid ambiguity
    /// when one identifier is a suffix of another.
    /// </summary>
    public static List<WispEntry> ComputeEntries(
        TouchMapperConfig config,
        List<MonitorInfo> monitors,
        List<TouchDeviceInfo> touches)
    {
        var results = new List<WispEntry>();
        var remaining = new HashSet<TouchDeviceInfo>(touches);

        // Process longest identifiers first — they are the most specific
        var sorted = config.Mappings.OrderByDescending(m => m.UsbIdentifier.Length);

        foreach (var mapping in sorted)
        {
            var touch = remaining.FirstOrDefault(t =>
                t.UsbLocationPath.EndsWith("#" + mapping.UsbIdentifier, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.UsbLocationPath, mapping.UsbIdentifier, StringComparison.OrdinalIgnoreCase));

            if (touch is null) continue;

            var monitor = monitors.FirstOrDefault(m =>
                string.Equals(m.EdidSerial, mapping.MonitorEdidSerial, StringComparison.OrdinalIgnoreCase));

            if (monitor is null) continue;

            remaining.Remove(touch);
            results.Add(new WispEntry
            {
                HidDevicePath = touch.HidDevicePath,
                DisplayDevicePath = monitor.DisplayDevicePath
            });
        }

        return results;
    }
}
