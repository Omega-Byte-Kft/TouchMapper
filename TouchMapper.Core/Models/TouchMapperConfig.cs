namespace TouchMapper.Core.Models;

public sealed class TouchMapperConfig
{
    public int SchemaVersion { get; init; } = 2;
    public List<TopologyGroup> TopologyGroups { get; init; } = [];
    public List<DirectMapping> DirectMappings { get; init; } = [];
}
