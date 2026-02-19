using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Agent;

public sealed class SpawnAgentTool : IOrcaTool
{
    public string Name => "spawn_agent";
    public string Description => "Launch a sub-agent to handle a focused task independently with its own conversation context and tool access. Use for parallel exploration, research, or independent subtasks (e.g., 'find all usages of X', 'summarize this module', 'refactor file Y').";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "task": {
                "type": "string",
                "description": "A clear description of the task for the sub-agent to accomplish"
            }
        },
        "required": ["task"]
    }
    """).RootElement;

    // The actual execution is handled by the orchestrator, wired in Program.cs.
    // This tool's ExecuteAsync is a placeholder that returns an error if not properly wired.
    public Func<string, CancellationToken, Task<string>>? AgentSpawner { get; set; }

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var task = args.GetProperty("task").GetString()!;

        if (AgentSpawner is null)
            return ToolResult.Error("Agent spawning not configured.");

        try
        {
            var result = await AgentSpawner(task, ct);
            return ToolResult.Success(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Sub-agent failed: {ex.Message}");
        }
    }
}
