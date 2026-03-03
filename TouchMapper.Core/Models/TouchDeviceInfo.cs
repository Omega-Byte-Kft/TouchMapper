namespace TouchMapper.Core.Models;

public sealed class TouchDeviceInfo
{
    /// <summary>PnP instance ID of the HID device, e.g. "HID\VID_1FD2&amp;PID_8102&amp;MI_00\A&amp;332852DA&amp;0&amp;0000"</summary>
    public required string PnpInstanceId { get; init; }

    /// <summary>
    /// HID device path for Wisp registry key,
    /// e.g. "\\?\HID#VID_1FD2&amp;PID_8102&amp;MI_00#a&amp;332852da&amp;0&amp;0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"
    /// </summary>
    public required string HidDevicePath { get; init; }

    /// <summary>USB location path from DEVPKEY_Device_LocationPaths on the composite USB parent</summary>
    public required string UsbLocationPath { get; init; }

    /// <summary>Number of USB hops from host controller (count of "#USB(" in location path)</summary>
    public int HopCount { get; init; }

    public override string ToString() => $"{PnpInstanceId} (hops={HopCount})";
}
