using System.Text.Json;
using System.Text.Json.Serialization;
using TouchMapper.Core.Models;

namespace TouchMapper.Core.Config;

public static class ConfigStore
{
    public static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "TouchMapper",
        "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool Exists() => File.Exists(ConfigPath);

    public static TouchMapperConfig? Load()
    {
        if (!Exists()) return null;
        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<TouchMapperConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(TouchMapperConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
