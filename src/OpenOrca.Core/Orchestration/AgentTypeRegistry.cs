namespace OpenOrca.Core.Orchestration;

/// <summary>
/// Registry of agent types. Built-in types are always available; custom types can be registered at runtime.
/// </summary>
public static class AgentTypeRegistry
{
    private static readonly Dictionary<string, AgentTypeDefinition> BuiltInTypes = new(StringComparer.OrdinalIgnoreCase)
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

    private static readonly Dictionary<string, AgentTypeDefinition> CustomTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a single custom agent type. Overwrites any existing custom type with the same name.
    /// </summary>
    public static void Register(AgentTypeDefinition def)
    {
        CustomTypes[def.Name] = def;
    }

    /// <summary>
    /// Register multiple custom agent types. Project types override global types.
    /// Custom types override built-in types with a same-name collision.
    /// </summary>
    public static void RegisterCustom(Dictionary<string, AgentTypeDefinition> customs)
    {
        foreach (var (name, def) in customs)
            CustomTypes[name] = def;
    }

    /// <summary>
    /// Clear all custom agent registrations (useful for testing).
    /// </summary>
    public static void ClearCustom() => CustomTypes.Clear();

    /// <summary>
    /// Resolve an agent type by name. Custom types take precedence over built-in types.
    /// Returns null if not found.
    /// </summary>
    public static AgentTypeDefinition? Resolve(string name)
    {
        // Custom types take precedence
        if (CustomTypes.TryGetValue(name, out var custom))
            return custom;

        return BuiltInTypes.GetValueOrDefault(name);
    }

    /// <summary>
    /// Returns the default agent type ("general").
    /// </summary>
    public static AgentTypeDefinition GetDefault() => BuiltInTypes["general"];

    /// <summary>
    /// Returns all registered agent types (built-in + custom, custom overrides built-in on collision).
    /// </summary>
    public static IReadOnlyList<AgentTypeDefinition> GetAll()
    {
        var merged = new Dictionary<string, AgentTypeDefinition>(BuiltInTypes, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, def) in CustomTypes)
            merged[name] = def;
        return merged.Values.ToList();
    }

    /// <summary>
    /// Returns all registered agent type names.
    /// </summary>
    public static IReadOnlyList<string> GetAllNames()
    {
        var names = new HashSet<string>(BuiltInTypes.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var name in CustomTypes.Keys)
            names.Add(name);
        return names.ToList();
    }
}
