using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Git;

public sealed class GitCheckoutTool : IOrcaTool
{
    public string Name => "git_checkout";
    public string Description => "Switch branches or restore working tree files. Set create=true to create and switch to a new branch in one step (-b flag).";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "target": {
                "type": "string",
                "description": "Branch name or commit to checkout"
            },
            "create": {
                "type": "boolean",
                "description": "Create a new branch (-b flag). Defaults to false."
            },
            "path": {
                "type": "string",
                "description": "The repository path. Defaults to current directory."
            }
        },
        "required": ["target"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var target = args.GetProperty("target").GetString()!;
        var create = args.TryGetProperty("create", out var c) && c.GetBoolean();
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";

        var flag = create ? "-b " : "";
        return await GitHelper.RunGitAsync($"checkout {flag}{target}", path, ct);
    }
}
