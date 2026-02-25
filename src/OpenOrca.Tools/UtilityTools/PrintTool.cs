using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.UtilityTools;

public sealed class PrintTool : IOrcaTool
{
    public string Name => "print";
    public string Description => "Display a message to the user. Use this to show results, status updates, or explanations. The message content will always be visible to the user regardless of their verbosity setting.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "message": {
                "type": "string",
                "description": "The message to display to the user"
            }
        },
        "required": ["message"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var message = args.GetProperty("message").GetString() ?? "";
        return Task.FromResult(ToolResult.Success(message));
    }
}
