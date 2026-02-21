using OpenOrca.Core.Configuration;

namespace OpenOrca.Cli.CustomCommands;

/// <summary>
/// Discovers and loads user-defined slash commands from .orca/commands/ and ~/.openorca/commands/.
/// Each .md file becomes a command: filename (minus extension) = command name.
/// </summary>
public sealed class CustomCommandLoader
{
    /// <summary>
    /// Scan project and global command directories. Returns name → file path.
    /// Project commands take priority over global commands with the same name.
    /// </summary>
    public Dictionary<string, string> DiscoverCommands()
    {
        var commands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Global commands first (can be overridden by project commands)
        var globalDir = Path.Combine(ConfigManager.GetConfigDirectory(), "commands");
        ScanDirectory(globalDir, commands);

        // Project commands (override global)
        var loader = new ProjectInstructionsLoader();
        var projectRoot = loader.FindProjectRoot(Directory.GetCurrentDirectory());
        if (projectRoot is not null)
        {
            var projectDir = Path.Combine(projectRoot, ".orca", "commands");
            ScanDirectory(projectDir, commands);
        }

        return commands;
    }

    /// <summary>
    /// Load a command template by name. Returns the file content, or null if not found.
    /// </summary>
    public async Task<string?> LoadTemplateAsync(string name)
    {
        var commands = DiscoverCommands();
        if (!commands.TryGetValue(name, out var path))
            return null;

        if (!File.Exists(path))
            return null;

        return await File.ReadAllTextAsync(path);
    }

    private static void ScanDirectory(string dir, Dictionary<string, string> commands)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(name))
                commands[name] = file;
        }
    }
}
