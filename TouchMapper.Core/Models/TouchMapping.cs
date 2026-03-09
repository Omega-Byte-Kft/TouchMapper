namespace TouchMapper.Core.Models;

public sealed class TouchMapping
{
    /// <summary>
    /// USB path identifier: the unique suffix from the divergence point for grouped devices,
    /// or the full path for standalone devices.
    /// e.g. "USB(1)#USB(2)#USBMI(0)" or "PCIROOT(0)#PCI(1400)#...#USBMI(0)".
    /// </summary>
    public required string UsbIdentifier { get; init; }

    public required string MonitorEdidSerial { get; init; }

    public string MonitorFriendlyName { get; set; } = "";
}
