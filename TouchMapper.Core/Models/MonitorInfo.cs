namespace TouchMapper.Core.Models;

public sealed class MonitorInfo
{
    /// <summary>Raw WMI InstanceName, e.g. "DISPLAY\HPN369A\4&36bcc35&0&UID41031_0"</summary>
    public required string InstanceName { get; init; }

    /// <summary>Decoded EDID serial number string, e.g. "CNK24605YR"</summary>
    public required string EdidSerial { get; init; }

    /// <summary>Monitor model code, e.g. "HPN369A"</summary>
    public required string MonitorModel { get; init; }

    /// <summary>User-friendly name from EDID, e.g. "HP E24t G4". May be empty if not available.</summary>
    public string UserFriendlyName { get; init; } = "";

    /// <summary>
    /// Display device path for Wisp registry value,
    /// e.g. "\\?\DISPLAY#HPN369A#4&36bcc35&0&UID41031#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"
    /// </summary>
    public required string DisplayDevicePath { get; init; }

    /// <summary>True for built-in laptop panels (GXTP, ELAN, CMN, LEN, SDC, BOE, etc.)</summary>
    public bool IsInternal { get; init; }

    /// <summary>Display label used in ComboBoxes and UI.</summary>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(UserFriendlyName)
            ? MonitorModel
            : UserFriendlyName;

    public override string ToString() =>
        $"{DisplayLabel} [{EdidSerial}]{(IsInternal ? " (internal)" : "")}";
}
