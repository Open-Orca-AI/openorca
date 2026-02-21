using System.Text.Json;
using OpenOrca.Core.Serialization;

namespace OpenOrca.Core.Configuration;

public sealed class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openorca");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public OrcaConfig Config { get; private set; } = new();

    public async Task LoadAsync()
    {
        if (!File.Exists(ConfigPath))
        {
            await SaveAsync();
            return;
        }

        var json = await File.ReadAllTextAsync(ConfigPath);
        Config = JsonSerializer.Deserialize(json, OrcaJsonContext.Default.OrcaConfig) ?? new OrcaConfig();
        ValidateConfig();
    }

    private void ValidateConfig()
    {
        if (string.IsNullOrWhiteSpace(Config.LmStudio.BaseUrl))
            Config.LmStudio.BaseUrl = "http://localhost:1234/v1";
        if (Config.LmStudio.Temperature is < 0 or > 2)
            Config.LmStudio.Temperature = 0.7f;
        if (Config.Context.ContextWindowSize <= 0)
            Config.Context.ContextWindowSize = 8192;
        if (Config.Context.AutoCompactThreshold is <= 0 or > 1)
            Config.Context.AutoCompactThreshold = 0.8f;
        if (Config.LmStudio.StreamingTimeoutSeconds <= 0)
            Config.LmStudio.StreamingTimeoutSeconds = 120;
        if (Config.Agent.MaxIterations <= 0)
            Config.Agent.MaxIterations = 15;
        if (Config.Agent.TimeoutSeconds <= 0)
            Config.Agent.TimeoutSeconds = 300;
        if (Config.Shell.IdleTimeoutSeconds < 0)
            Config.Shell.IdleTimeoutSeconds = 15;
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(Config, OrcaJsonContext.Default.OrcaConfig);
        await File.WriteAllTextAsync(ConfigPath, json);
    }

    public static string GetConfigDirectory() => ConfigDir;
}
