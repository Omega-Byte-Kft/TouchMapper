using TouchMapper.Core.Models;

namespace TouchMapper.Configurator;

/// <summary>Shared mutable state passed between wizard pages.</summary>
public sealed class WizardState
{
    public List<MonitorInfo> Monitors { get; set; } = [];
    public List<TouchDeviceInfo> TouchDevices { get; set; } = [];

    /// <summary>
    /// Live topology groups discovered by TopologyAnalyzer, parallel to ConfigGroups.
    /// Only groups with 2+ devices (chained/daisy-chained).
    /// Index i → list of touch devices for ConfigGroups[i], sorted by hop count.
    /// </summary>
    public List<List<TouchDeviceInfo>> LiveTouchGroups { get; set; } = [];

    /// <summary>Standalone touch devices (groups of 1) that aren't part of a daisy chain.</summary>
    public List<TouchDeviceInfo> StandaloneTouchDevices { get; set; } = [];

    /// <summary>The config being built by the wizard.</summary>
    public TouchMapperConfig Config { get; set; } = new();
}
