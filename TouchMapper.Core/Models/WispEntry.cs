namespace TouchMapper.Core.Models;

/// <summary>A single touch→display mapping to write into the Wisp Digimon registry key.</summary>
public sealed class WispEntry
{
    /// <summary>
    /// HID device path used as part of the registry value name,
    /// e.g. "\\?\HID#VID_1FD2&amp;PID_8102&amp;MI_00#a&amp;332852da&amp;0&amp;0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"
    /// </summary>
    public required string HidDevicePath { get; init; }

    /// <summary>
    /// Display device path stored as the registry value,
    /// e.g. "\\?\DISPLAY#HPN369A#4&amp;36bcc35&amp;0&amp;UID41031#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"
    /// </summary>
    public required string DisplayDevicePath { get; init; }
}
