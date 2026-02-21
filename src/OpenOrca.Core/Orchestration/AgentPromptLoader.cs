using System.Reflection;

namespace OpenOrca.Core.Orchestration;

/// <summary>
/// Loads embedded agent prompt templates from assembly resources and substitutes template variables.
/// </summary>
public sealed class AgentPromptLoader
{
    private const string ResourcePrefix = "OpenOrca.Core.Orchestration.Prompts.";

    /// <summary>
    /// Load the prompt for the given agent type, substituting template variables.
    /// Returns null if the embedded resource is not found.
    /// </summary>
    public string? LoadPrompt(AgentTypeDefinition agentType, string task, string cwd, string platform)
    {
        var resourceName = ResourcePrefix + agentType.PromptResourceName;
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        var template = reader.ReadToEnd();

        return template
            .Replace("{{TASK}}", task)
            .Replace("{{CWD}}", cwd)
            .Replace("{{PLATFORM}}", platform);
    }
}
