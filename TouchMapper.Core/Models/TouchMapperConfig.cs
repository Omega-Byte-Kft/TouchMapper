namespace TouchMapper.Core.Models;

public sealed class TouchMapperConfig
{
    public int SchemaVersion { get; init; } = 3;
    public List<TouchMapping> Mappings { get; init; } = [];
}
