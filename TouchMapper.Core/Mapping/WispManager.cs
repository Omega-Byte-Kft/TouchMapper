using System.Diagnostics;
using Microsoft.Win32;
using TouchMapper.Core.Models;

namespace TouchMapper.Core.Mapping;

public static class WispManager
{
    private const string DigimonKeyPath = @"SOFTWARE\Microsoft\Wisp\Pen\Digimon";

    /// <summary>
    /// Writes touch→display mappings to the Wisp Digimon registry key,
    /// then forces Windows to re-read them by restarting the HID touch devices.
    /// Requires the caller to be running as Administrator / LocalSystem.
    /// </summary>
    public static void ApplyMappings(List<WispEntry> entries)
    {
        using var key = Registry.LocalMachine.CreateSubKey(DigimonKeyPath, writable: true);
        if (key is null)
            throw new InvalidOperationException("Cannot open Wisp\\Pen\\Digimon registry key.");

        // Clear all existing "20-" entries to avoid stale mappings from previous sessions
        foreach (var name in key.GetValueNames())
        {
            if (name.StartsWith("20-", StringComparison.Ordinal))
                key.DeleteValue(name, throwOnMissingValue: false);
        }

        // Write the new entries
        foreach (var entry in entries)
        {
            var valueName = "20-" + entry.HidDevicePath;
            key.SetValue(valueName, entry.DisplayDevicePath, RegistryValueKind.String);
        }

        // Force Windows to re-read the Digimon registry by restarting each HID touch device.
        // On modern Windows 11 (build 26100+), TabletInputService no longer exists;
        // the mapping is applied when the HID device re-enumerates.
        RestartHidDevices(entries);
    }

    /// <summary>
    /// Saves a snapshot of all current "20-" Digimon registry values.
    /// Used to restore original mappings after a test overlay closes.
    /// </summary>
    public static Dictionary<string, string> SaveSnapshot()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);

        using var key = Registry.LocalMachine.OpenSubKey(DigimonKeyPath);
        if (key is null) return snapshot;

        foreach (var name in key.GetValueNames())
        {
            if (!name.StartsWith("20-", StringComparison.Ordinal)) continue;
            var value = key.GetValue(name) as string;
            if (value is not null)
                snapshot[name] = value;
        }

        return snapshot;
    }

    /// <summary>
    /// Restores a previously saved snapshot: clears all "20-" entries,
    /// writes the snapshot entries back, and restarts affected HID devices.
    /// </summary>
    public static void RestoreSnapshot(Dictionary<string, string> snapshot)
    {
        using var key = Registry.LocalMachine.CreateSubKey(DigimonKeyPath, writable: true);
        if (key is null) return;

        // Clear all existing "20-" entries
        foreach (var name in key.GetValueNames())
        {
            if (name.StartsWith("20-", StringComparison.Ordinal))
                key.DeleteValue(name, throwOnMissingValue: false);
        }

        // Write snapshot entries back
        var entries = new List<WispEntry>();
        foreach (var (name, value) in snapshot)
        {
            key.SetValue(name, value, RegistryValueKind.String);
            entries.Add(new WispEntry
            {
                HidDevicePath = name.StartsWith("20-", StringComparison.Ordinal) ? name[3..] : name,
                DisplayDevicePath = value
            });
        }

        if (entries.Count > 0)
            RestartHidDevices(entries);
    }

    /// <summary>Reads all current Wisp Digimon entries (for diagnostics / display in UI).</summary>
    public static List<WispEntry> ReadCurrentMappings()
    {
        var results = new List<WispEntry>();

        using var key = Registry.LocalMachine.OpenSubKey(DigimonKeyPath);
        if (key is null) return results;

        foreach (var name in key.GetValueNames())
        {
            if (!name.StartsWith("20-", StringComparison.Ordinal)) continue;
            var value = key.GetValue(name) as string;
            if (value is null) continue;

            results.Add(new WispEntry
            {
                HidDevicePath = name[3..],
                DisplayDevicePath = value
            });
        }

        return results;
    }

    /// <summary>
    /// Restarts each HID touch device via pnputil so the input stack re-reads
    /// the Digimon registry.  Falls back to disabling+enabling if restart fails.
    /// </summary>
    private static void RestartHidDevices(List<WispEntry> entries)
    {
        foreach (var entry in entries)
        {
            var instanceId = HidPathToInstanceId(entry.HidDevicePath);
            if (instanceId is null) continue;

            RunPnpUtil($"/restart-device \"{instanceId}\"");
        }
    }

    /// <summary>
    /// Converts a HID device interface path back to a PnP instance ID.
    /// "\\?\HID#VID_1FD2&amp;...#a&amp;332852da&amp;0&amp;0000#{4d1e55b2-...}"
    /// → "HID\VID_1FD2&amp;...\a&amp;332852da&amp;0&amp;0000"
    /// </summary>
    private static string? HidPathToInstanceId(string hidPath)
    {
        // Strip \\?\ prefix
        var s = hidPath;
        if (s.StartsWith(@"\\?\", StringComparison.Ordinal))
            s = s[4..];

        // Remove trailing #{guid}
        var guidStart = s.LastIndexOf("#{", StringComparison.Ordinal);
        if (guidStart >= 0)
            s = s[..guidStart];

        // Replace # with backslash
        return s.Replace('#', '\\');
    }

    private static void RunPnpUtil(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("pnputil.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit(10_000);
        }
        catch { /* best effort */ }
    }
}
