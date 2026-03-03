using TouchMapper.Core.Models;

namespace TouchMapper.Core.Mapping;

public static class MappingEngine
{
    /// <summary>
    /// Computes Wisp entries by matching topology groups in the config to the live
    /// detected touch devices and monitors.
    /// </summary>
    public static List<WispEntry> ComputeEntries(
        TouchMapperConfig config,
        List<MonitorInfo> monitors,
        List<TouchDeviceInfo> touches)
    {
        var results = new List<WispEntry>();

        // Group live touch devices by shared USB ancestor
        var liveGroups = TopologyAnalyzer.GroupByAncestor(touches);

        foreach (var cfgGroup in config.TopologyGroups)
        {
            // Build ordered list of EDID serials for this config group:
            // [anchor, child0, child1, ...]
            var orderedSerials = new List<string> { cfgGroup.AnchorEdidSerial };
            orderedSerials.AddRange(cfgGroup.Children.Select(c => c.EdidSerial));

            // Find the matching live touch group: must have at least as many devices
            // as the config group specifies, and be sorted by hops.
            // We pick the group whose size best matches.
            var matchingTouchGroup = FindBestMatchingGroup(liveGroups, orderedSerials.Count);
            if (matchingTouchGroup is null) continue;

            // Match touch[i] → monitor with orderedSerials[i]
            for (var i = 0; i < Math.Min(matchingTouchGroup.Count, orderedSerials.Count); i++)
            {
                var touch = matchingTouchGroup[i];
                var edid = orderedSerials[i];

                var monitor = monitors.FirstOrDefault(m =>
                    string.Equals(m.EdidSerial, edid, StringComparison.OrdinalIgnoreCase));

                if (monitor is null) continue;

                results.Add(new WispEntry
                {
                    HidDevicePath = touch.HidDevicePath,
                    DisplayDevicePath = monitor.DisplayDevicePath
                });
            }
        }

        // Process DirectMappings (standalone touch devices)
        foreach (var dm in config.DirectMappings)
        {
            // Find live touch device whose UsbLocationPath matches (prefix match for stability)
            var touch = touches.FirstOrDefault(t =>
                t.UsbLocationPath.StartsWith(dm.UsbLocationPath, StringComparison.OrdinalIgnoreCase)
                || dm.UsbLocationPath.StartsWith(t.UsbLocationPath, StringComparison.OrdinalIgnoreCase));

            if (touch is null) continue;

            var monitor = monitors.FirstOrDefault(m =>
                string.Equals(m.EdidSerial, dm.MonitorEdidSerial, StringComparison.OrdinalIgnoreCase));

            if (monitor is null) continue;

            results.Add(new WispEntry
            {
                HidDevicePath = touch.HidDevicePath,
                DisplayDevicePath = monitor.DisplayDevicePath
            });
        }

        return results;
    }

    /// <summary>
    /// Finds the live touch group whose size is >= required count.
    /// Prefers the group whose size exactly matches; otherwise picks the smallest group
    /// that is still large enough.
    /// </summary>
    private static List<TouchDeviceInfo>? FindBestMatchingGroup(
        List<List<TouchDeviceInfo>> groups, int requiredCount)
    {
        var candidates = groups
            .Where(g => g.Count >= requiredCount)
            .OrderBy(g => g.Count)
            .ToList();

        return candidates.Count > 0 ? candidates[0] : null;
    }
}
