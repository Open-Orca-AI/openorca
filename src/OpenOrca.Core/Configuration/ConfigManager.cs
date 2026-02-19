using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenOrca.Core.Configuration;

public sealed class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openorca");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OrcaConfig Config { get; private set; } = new();

    public async Task LoadAsync()
    {
        if (!File.Exists(ConfigPath))
        {
            await SaveAsync();
            return;
        }

        var json = await File.ReadAllTextAsync(ConfigPath);
        Config = JsonSerializer.Deserialize<OrcaConfig>(json, JsonOptions) ?? new OrcaConfig();
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(Config, JsonOptions);
        await File.WriteAllTextAsync(ConfigPath, json);
    }

    public static string GetConfigDirectory() => ConfigDir;
}
