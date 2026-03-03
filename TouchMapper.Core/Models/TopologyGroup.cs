namespace TouchMapper.Core.Models;

public sealed class TopologyGroup
{
    /// <summary>EDID serial of the anchor monitor (fewest USB hops; others plug into it).</summary>
    public required string AnchorEdidSerial { get; init; }

    public required string AnchorFriendlyName { get; set; }

    /// <summary>Child monitors in hop order (index 0 = first hop beyond anchor).</summary>
    public List<MonitorProfile> Children { get; init; } = [];
}
