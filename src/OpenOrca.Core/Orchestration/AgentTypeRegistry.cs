namespace OpenOrca.Core.Orchestration;

/// <summary>
/// Static registry of built-in agent types. Provides lookup by name and enumeration for prompt generation.
/// </summary>
public static class AgentTypeRegistry
{
    private static readonly Dictionary<string, AgentTypeDefinition> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explore"] = new AgentTypeDefinition(
            Name: "explore",
            Description: "Fast read-only codebase exploration — find files, search code, understand structure",
            AllowedTools: ["read_file", "list_directory", "glob", "grep", "think"],
            PromptResourceName: "explore.md"),

        ["plan"] = new AgentTypeDefinition(
            Name: "plan",
            Description: "Architecture and implementation planning — research and design before coding",
            AllowedTools: ["read_file", "list_directory", "glob", "grep", "think", "web_search", "web_fetch"],
            PromptResourceName: "plan.md"),

        ["bash"] = new AgentTypeDefinition(
            Name: "bash",
            Description: "Command execution specialist — run shell commands, manage processes",
            AllowedTools: ["bash", "read_file", "think", "get_process_output", "start_background_process", "stop_process", "env"],
            PromptResourceName: "bash.md"),

        ["review"] = new AgentTypeDefinition(
            Name: "review",
            Description: "Code review and diff analysis — examine changes, check quality, find issues",
            AllowedTools: ["read_file", "list_directory", "glob", "grep", "git_diff", "git_log", "git_status", "think"],
            PromptResourceName: "review.md"),

        ["general"] = new AgentTypeDefinition(
            Name: "general",
            Description: "General-purpose agent with full tool access — use when no specialized type fits",
            AllowedTools: null,
            PromptResourceName: "general.md")
    };

    /// <summary>
    /// Resolve an agent type by name. Returns null if not found.
    /// </summary>
    public static AgentTypeDefinition? Resolve(string name)
    {
        return Types.GetValueOrDefault(name);
    }

    /// <summary>
    /// Returns the default agent type ("general").
    /// </summary>
    public static AgentTypeDefinition GetDefault() => Types["general"];

    /// <summary>
    /// Returns all registered agent types.
    /// </summary>
    public static IReadOnlyList<AgentTypeDefinition> GetAll() => Types.Values.ToList();
}
