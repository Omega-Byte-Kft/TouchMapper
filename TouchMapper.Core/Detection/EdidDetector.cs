using System.Management;
using System.Text;
using TouchMapper.Core.Models;

namespace TouchMapper.Core.Detection;

public static class EdidDetector
{
    // GUID for monitor display device interface
    private const string MonitorGuid = "{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    // Built-in panel manufacturer codes (laptop screens)
    private static readonly string[] InternalVendors =
        ["ELAN", "GXTP", "CMN", "LEN", "SDC", "BOE", "AUO", "SHP", "IVO"];

    public static List<MonitorInfo> GetActiveMonitors()
    {
        var results = new List<MonitorInfo>();

        using var searcher = new ManagementObjectSearcher(
            @"root\wmi",
            "SELECT * FROM WmiMonitorID WHERE Active=TRUE");

        foreach (ManagementObject obj in searcher.Get())
        {
            var instanceName = (string)obj["InstanceName"];
            // InstanceName looks like "DISPLAY\HPN369A\4&36bcc35&0&UID41031_0"
            // Strip trailing "_0" suffix that WMI appends
            var cleanInstance = instanceName.TrimEnd('_', '0').TrimEnd('_');

            var serialBytes = obj["SerialNumberID"] as ushort[];
            var edidSerial = DecodeEdidString(serialBytes);

            // Extract monitor model from instance path segment
            var parts = cleanInstance.Split('\\');
            var monitorModel = parts.Length >= 2 ? parts[1] : cleanInstance;

            // Build display device path for Wisp registry
            // "DISPLAY\HPN369A\4&36bcc35&0&UID41031" → "\\?\DISPLAY#HPN369A#4&36bcc35&0&UID41031#{guid}"
            var displayPath = BuildDisplayDevicePath(cleanInstance, MonitorGuid);

            var isInternal = InternalVendors.Any(v =>
                instanceName.Contains(v, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(edidSerial))
            {
                results.Add(new MonitorInfo
                {
                    InstanceName = cleanInstance,
                    EdidSerial = edidSerial,
                    MonitorModel = monitorModel,
                    DisplayDevicePath = displayPath,
                    IsInternal = isInternal
                });
            }
        }

        return results;
    }

    private static string DecodeEdidString(ushort[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Converts WMI InstanceName to the display device path format used in the Wisp registry.
    /// "DISPLAY\HPN369A\4&amp;36bcc35&amp;0&amp;UID41031" →
    /// "\\?\DISPLAY#HPN369A#4&amp;36bcc35&amp;0&amp;UID41031#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"
    /// </summary>
    private static string BuildDisplayDevicePath(string instanceName, string guid)
    {
        // Replace backslashes with # and prefix with \\?\
        var withHash = instanceName.Replace('\\', '#');
        return $@"\\?\{withHash}#{guid}";
    }
}
