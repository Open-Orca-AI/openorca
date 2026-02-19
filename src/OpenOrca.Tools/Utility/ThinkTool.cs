using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Utility;

public sealed class ThinkTool : IOrcaTool
{
    public string Name => "think";
    public string Description => "Use this tool to think through a problem step-by-step before acting. Returns the thought verbatim. No side effects. Especially useful for planning complex multi-step tasks, analyzing errors, or reasoning about code structure before making changes.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "thought": {
                "type": "string",
                "description": "Your step-by-step reasoning, analysis, or plan"
            }
        },
        "required": ["thought"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var thought = args.GetProperty("thought").GetString()!;
        return Task.FromResult(ToolResult.Success(thought));
    }
}
