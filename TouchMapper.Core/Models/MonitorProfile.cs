namespace TouchMapper.Core.Models;

public sealed class MonitorProfile
{
    public required string EdidSerial { get; init; }
    public required string FriendlyName { get; set; }

    /// <summary>True if this is the anchor monitor (the one others daisy-chain through).</summary>
    public bool IsAnchor { get; set; }
}
