namespace TouchMapper.Core.Models;

public sealed class DirectMapping
{
    public required string UsbLocationPath { get; init; }
    public required string MonitorEdidSerial { get; init; }
    public required string MonitorFriendlyName { get; set; }
}
