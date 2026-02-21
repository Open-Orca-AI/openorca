namespace OpenOrca.Core.Orchestration;

/// <summary>
/// Defines a typed sub-agent: its name, LLM-facing description, allowed tools, and prompt resource.
/// </summary>
public sealed record AgentTypeDefinition(
    string Name,
    string Description,
    IReadOnlyList<string>? AllowedTools,
    string PromptResourceName);
