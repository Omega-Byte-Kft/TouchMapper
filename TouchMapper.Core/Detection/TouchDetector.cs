using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using TouchMapper.Core.Models;

namespace TouchMapper.Core.Detection;

public static class TouchDetector
{
    // HID device interface GUID (used in Wisp key name and device path)
    private const string HidGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";

    // DEVPKEY_Device_LocationPaths = {a45c254e-df1c-4efd-8020-67d146a850e0}, PID 37
    private static DevPropKey LocationPathsKey = new(
        new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 37);

    private const int CR_SUCCESS = 0;

    public static List<TouchDeviceInfo> GetActiveTouchDevices()
    {
        var results = new List<TouchDeviceInfo>();

        foreach (var hidId in EnumerateTouchHidInstances())
        {
            var locationPath = GetUsbParentLocationPath(hidId);
            if (locationPath is null) continue;

            results.Add(new TouchDeviceInfo
            {
                PnpInstanceId = hidId,
                HidDevicePath = BuildHidDevicePath(hidId),
                UsbLocationPath = locationPath,
                HopCount = CountUsbHops(locationPath)
            });
        }

        return results;
    }

    /// <summary>
    /// Enumerates HKLM\SYSTEM\CurrentControlSet\Enum\HID for devices whose
    /// HardwareID or DeviceDesc indicates a touch digitizer.
    /// Includes both USB composite (MI_00) and I2C/ACPI touch screens.
    /// </summary>
    private static IEnumerable<string> EnumerateTouchHidInstances()
    {
        using var hidRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\HID");
        if (hidRoot is null) yield break;

        foreach (var hwId in hidRoot.GetSubKeyNames())
        {
            using var hwKey = hidRoot.OpenSubKey(hwId);
            if (hwKey is null) continue;

            foreach (var inst in hwKey.GetSubKeyNames())
            {
                using var instKey = hwKey.OpenSubKey(inst);
                if (instKey is null) continue;

                if (IsTouchDevice(instKey))
                    yield return $"HID\\{hwId}\\{inst}";
            }
        }
    }

    private static bool IsTouchDevice(RegistryKey instKey)
    {
        // HardwareID is a REG_MULTI_SZ. Touch screens include "HID_DEVICE_UP:000D_U:0004"
        // (HID Usage Page 0x0D = Digitizers, Usage 0x04 = Touch Screen)
        if (instKey.GetValue("HardwareID") is string[] hwIds)
        {
            foreach (var id in hwIds)
            {
                if (id.Contains("UP:000D", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("touch",   StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // Fallback: description string
        var desc = instKey.GetValue("DeviceDesc") as string ?? string.Empty;
        // DeviceDesc is sometimes "@oem.inf,%strkey%;Friendly Name" — strip the prefix
        var semicolon = desc.LastIndexOf(';');
        if (semicolon >= 0) desc = desc[(semicolon + 1)..];

        return desc.Contains("touch",     StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("digitizer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walks the device tree from the HID device upward via CfgMgr32
    /// until it finds a device that has a LocationPaths property.
    /// Handles USB composite devices, I2C-HID, and ACPI-connected touch screens.
    /// </summary>
    private static string? GetUsbParentLocationPath(string hidInstanceId)
    {
        if (NativeMethods.CM_Locate_DevNode(out var node, hidInstanceId, 0) != CR_SUCCESS)
            return null;

        // First try: get LocationPath from the HID device node itself
        var selfPath = GetLocationPath(node);
        if (selfPath is not null) return selfPath;

        var current = node;
        for (var depth = 0; depth < 8; depth++)
        {
            if (NativeMethods.CM_Get_Parent(out var parent, current, 0) != CR_SUCCESS)
                break;

            var parentId = GetDeviceId(parent);
            if (parentId is null) break;

            // Try to get LocationPath from any parent that has one
            var path = GetLocationPath(parent);
            if (path is not null) return path;

            // Stop climbing once we've reached the bus root
            if (parentId.StartsWith("PCIROOT(", StringComparison.OrdinalIgnoreCase))
                break;

            current = parent;
        }

        return null;
    }

    private static string? GetDeviceId(uint devNode)
    {
        var sb = new StringBuilder(512);
        return NativeMethods.CM_Get_Device_ID(devNode, sb, sb.Capacity, 0) == CR_SUCCESS
            ? sb.ToString()
            : null;
    }

    private static string? GetLocationPath(uint devNode)
    {
        var key = LocationPathsKey;
        var buffer = new byte[4096];
        uint bufSize = (uint)buffer.Length;

        if (NativeMethods.CM_Get_DevNode_Property(
                devNode, ref key, out _, buffer, ref bufSize, 0) != CR_SUCCESS)
            return null;

        if (bufSize < 2) return null;

        // DEVPROP_TYPE_STRING_LIST: null-separated UTF-16 strings.  Take the first entry.
        var decoded = Encoding.Unicode.GetString(buffer, 0, (int)bufSize);
        return decoded.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    public static int CountUsbHops(string locationPath)
    {
        var count = 0;
        var idx = 0;
        while ((idx = locationPath.IndexOf("#USB(", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += 5;
        }
        return count;
    }

    private static string BuildHidDevicePath(string instanceId)
        => $@"\\?\{instanceId.Replace('\\', '#')}#{HidGuid}";

    // ── P/Invoke ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey(Guid g, uint p)
    {
        public Guid fmtid = g;
        public uint pid   = p;
    }

    private static class NativeMethods
    {
        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")]
        internal static extern int CM_Locate_DevNode(
            out uint pdnDevInst, string pDeviceID, uint ulFlags);

        [DllImport("CfgMgr32.dll")]
        internal static extern int CM_Get_Parent(
            out uint pdnDevInst, uint dnDevInst, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_IDW")]
        internal static extern int CM_Get_Device_ID(
            uint dnDevInst, StringBuilder buffer, int bufferLen, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_DevNode_PropertyW")]
        internal static extern int CM_Get_DevNode_Property(
            uint dnDevInst,
            ref DevPropKey propertyKey,
            out uint propertyType,
            [Out] byte[] propertyBuffer,
            ref uint propertyBufferSize,
            uint ulFlags);
    }
}
