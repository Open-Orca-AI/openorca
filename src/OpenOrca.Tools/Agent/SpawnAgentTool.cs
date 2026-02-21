using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Agent;

public sealed class SpawnAgentTool : IOrcaTool
{
    public string Name => "spawn_agent";

    public string Description => ToolDescription;

    // Extracted to a non-interpolated const to avoid raw string brace issues
    private const string ToolDescription =
        "Launch a specialized sub-agent to handle a focused task autonomously with its own conversation context.\n" +
        "\n" +
        "Available agent types:\n" +
        "- explore: Fast read-only codebase exploration specialist. Has access to: read_file, list_directory, glob, grep, think. " +
            "Use for finding files, searching code, understanding project structure, tracing usages, and answering questions about the codebase. " +
            "PREFER this over calling glob/grep/read_file directly when the task requires broad exploration or more than 3 search queries.\n" +
        "- plan: Architecture and implementation planning. Has access to: read_file, list_directory, glob, grep, think, web_search, web_fetch. " +
            "Use for designing solutions, researching approaches, and producing step-by-step implementation plans.\n" +
        "- bash: Command execution specialist. Has access to: bash, read_file, think, get_process_output, start_background_process, stop_process, env. " +
            "Use for running builds, tests, scripts, and managing processes.\n" +
        "- review: Code review and diff analysis. Has access to: read_file, list_directory, glob, grep, git_diff, git_log, git_status, think. " +
            "Use for examining changes, checking code quality, and reviewing diffs.\n" +
        "- general: Full tool access (default). Use when no specialized type fits or the task requires both reading and writing.\n" +
        "\n" +
        "When to use spawn_agent:\n" +
        "- Broad codebase exploration (\"find all usages of X\", \"what is the project structure?\", \"how does Y work?\")\n" +
        "- Tasks requiring multiple rounds of search/read (more than 3 queries)\n" +
        "- Independent subtasks that can run in parallel with your main work\n" +
        "- Research or investigation before making changes\n" +
        "\n" +
        "When NOT to use spawn_agent:\n" +
        "- Reading a specific file you already know the path to — use read_file directly\n" +
        "- Searching for a specific class or function name — use glob or grep directly\n" +
        "- Investigating code within 2-3 known files — use read_file directly\n" +
        "- Simple single-step tasks that don't need their own conversation context\n" +
        "\n" +
        "Examples:\n" +
        "- User asks \"find all usages of AgentOrchestrator\" → spawn explore agent\n" +
        "- User asks \"what is the codebase structure?\" → spawn explore agent\n" +
        "- User asks \"where are errors from the client handled?\" → spawn explore agent\n" +
        "- User asks \"run the tests and report results\" → spawn bash agent\n" +
        "- User asks \"review the changes on this branch\" → spawn review agent\n" +
        "- User asks \"design how we should implement feature X\" → spawn plan agent";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "task": {
                "type": "string",
                "description": "A clear, detailed description of the task for the sub-agent. Include all necessary context — the agent starts a fresh conversation and cannot see your prior messages."
            },
            "agent_type": {
                "type": "string",
                "enum": ["explore", "plan", "bash", "review", "general"],
                "description": "The type of sub-agent to spawn. Determines the system prompt and available tools. Use 'explore' for codebase search/understanding, 'plan' for architecture/design, 'bash' for command execution, 'review' for code review/diff analysis, 'general' for full tool access (default)."
            }
        },
        "required": ["task"]
    }
    """).RootElement;

    // The actual execution is handled by the orchestrator, wired in Program.cs.
    // Delegate signature: (task, agentType, ct) => result
    public Func<string, string, CancellationToken, Task<string>>? AgentSpawner { get; set; }

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var task = args.GetProperty("task").GetString()!;
        var agentType = args.TryGetProperty("agent_type", out var atProp) && atProp.ValueKind == JsonValueKind.String
            ? atProp.GetString()!
            : "general";

        if (AgentSpawner is null)
            return ToolResult.Error("Agent spawning not configured.");

        try
        {
            var result = await AgentSpawner(task, agentType, ct);
            return ToolResult.Success(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Sub-agent failed: {ex.Message}");
        }
    }
}
