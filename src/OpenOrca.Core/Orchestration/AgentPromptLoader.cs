using System.Reflection;

namespace OpenOrca.Core.Orchestration;

/// <summary>
/// Loads agent prompt templates from embedded resources or file paths, and substitutes template variables.
/// </summary>
public sealed class AgentPromptLoader
{
    private const string ResourcePrefix = "OpenOrca.Core.Orchestration.Prompts.";

    /// <summary>
    /// Load the prompt for the given agent type, substituting template variables.
    /// If PromptResourceName is an absolute or relative file path, loads from the filesystem.
    /// Otherwise, loads from embedded resources.
    /// Returns null if the resource/file is not found.
    /// </summary>
    public string? LoadPrompt(AgentTypeDefinition agentType, string task, string cwd, string platform)
    {
        string? template;

        if (IsFilePath(agentType.PromptResourceName))
        {
            template = LoadFromFile(agentType.PromptResourceName);
        }
        else
        {
            template = LoadFromEmbeddedResource(agentType.PromptResourceName);
        }

        if (template is null)
            return null;

        return CustomAgentLoader.SubstituteTemplateVars(template, task, cwd, platform);
    }

    /// <summary>
    /// Determine if a PromptResourceName refers to a file path (vs embedded resource).
    /// File paths contain directory separators or are absolute paths.
    /// Embedded resources are bare filenames like "review.md".
    /// </summary>
    private static bool IsFilePath(string resourceName)
    {
        return resourceName.Contains(Path.DirectorySeparatorChar)
            || resourceName.Contains(Path.AltDirectorySeparatorChar)
            || Path.IsPathRooted(resourceName);
    }

    private static string? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var content = File.ReadAllText(filePath);

        // Strip YAML frontmatter if present (the body is the prompt template)
        var fmEnd = content.IndexOf("\n---\n", content.IndexOf("---\n", StringComparison.Ordinal) + 4, StringComparison.Ordinal);
        if (content.StartsWith("---") && fmEnd >= 0)
        {
            content = content[(fmEnd + 5)..]; // Skip past the closing ---\n
        }

        return content.TrimStart();
    }

    private static string? LoadFromEmbeddedResource(string resourceName)
    {
        var fullResourceName = ResourcePrefix + resourceName;
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(fullResourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
