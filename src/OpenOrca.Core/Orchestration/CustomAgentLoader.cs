using System.Text.RegularExpressions;
using OpenOrca.Core.Configuration;

namespace OpenOrca.Core.Orchestration;

/// <summary>
/// Discovers and parses custom agent definitions from .orca/agents/ and ~/.openorca/agents/.
/// Each .md file with YAML frontmatter becomes a custom agent type.
/// </summary>
public sealed partial class CustomAgentLoader
{
    // Matches YAML frontmatter delimited by --- lines at the start of the file
    [GeneratedRegex(@"\A---\s*\n(.*?)\n---\s*\n", RegexOptions.Singleline)]
    private static partial Regex FrontmatterPattern();

    // Simple YAML key-value extraction patterns
    [GeneratedRegex(@"^name:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^description:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex DescriptionPattern();

    [GeneratedRegex(@"^tools:\s*\[([^\]]*)\]", RegexOptions.Multiline)]
    private static partial Regex ToolsPattern();

    /// <summary>
    /// Discover custom agent definitions from global and project directories.
    /// Project agents override global agents with the same name.
    /// Returns name → AgentTypeDefinition dictionary.
    /// </summary>
    public Dictionary<string, AgentTypeDefinition> DiscoverAgents(IReadOnlyCollection<string>? registeredToolNames = null)
    {
        var agents = new Dictionary<string, AgentTypeDefinition>(StringComparer.OrdinalIgnoreCase);

        // Global agents first (can be overridden by project agents)
        var globalDir = Path.Combine(ConfigManager.GetConfigDirectory(), "agents");
        ScanDirectory(globalDir, agents, registeredToolNames);

        // Project agents (override global)
        var loader = new ProjectInstructionsLoader();
        var projectRoot = loader.FindProjectRoot(Directory.GetCurrentDirectory());
        if (projectRoot is not null)
        {
            var projectDir = Path.Combine(projectRoot, ".orca", "agents");
            ScanDirectory(projectDir, agents, registeredToolNames);
        }

        return agents;
    }

    /// <summary>
    /// Parse a single agent definition file. Returns null if the file is malformed.
    /// Exposed for testing.
    /// </summary>
    public AgentTypeDefinition? ParseAgentFile(string filePath, IReadOnlyCollection<string>? registeredToolNames = null)
    {
        if (!File.Exists(filePath))
            return null;

        var content = File.ReadAllText(filePath);
        return ParseAgentContent(content, filePath, registeredToolNames);
    }

    /// <summary>
    /// Parse agent definition content. Exposed for testing without file system.
    /// </summary>
    public AgentTypeDefinition? ParseAgentContent(string content, string filePath, IReadOnlyCollection<string>? registeredToolNames = null)
    {
        var fmMatch = FrontmatterPattern().Match(content);
        if (!fmMatch.Success)
            return null;

        var frontmatter = fmMatch.Groups[1].Value;
        var body = content[(fmMatch.Index + fmMatch.Length)..];

        // Extract required fields
        var nameMatch = NamePattern().Match(frontmatter);
        var descMatch = DescriptionPattern().Match(frontmatter);
        var toolsMatch = ToolsPattern().Match(frontmatter);

        if (!nameMatch.Success || !descMatch.Success || !toolsMatch.Success)
            return null;

        var name = nameMatch.Groups[1].Value.Trim();
        var description = descMatch.Groups[1].Value.Trim();
        var toolsRaw = toolsMatch.Groups[1].Value;

        var tools = toolsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('"', '\'', ' '))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        // Validate tool names if a registry is available
        if (registeredToolNames is not null)
        {
            var validTools = new HashSet<string>(registeredToolNames, StringComparer.OrdinalIgnoreCase);
            tools = tools.Where(t => validTools.Contains(t)).ToList();
        }

        if (tools.Count == 0)
            return null;

        // Use the file path as the PromptResourceName — AgentPromptLoader will detect it
        return new AgentTypeDefinition(
            Name: name,
            Description: description,
            AllowedTools: tools.AsReadOnly(),
            PromptResourceName: filePath);
    }

    /// <summary>
    /// Substitute template variables in a custom agent prompt.
    /// </summary>
    public static string SubstituteTemplateVars(string template, string task, string cwd, string platform)
    {
        return template
            .Replace("{{TASK}}", task)
            .Replace("{{CWD}}", cwd)
            .Replace("{{PLATFORM}}", platform);
    }

    private void ScanDirectory(string dir, Dictionary<string, AgentTypeDefinition> agents,
        IReadOnlyCollection<string>? registeredToolNames)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
        {
            var def = ParseAgentFile(file, registeredToolNames);
            if (def is not null)
                agents[def.Name] = def;
        }
    }
}
