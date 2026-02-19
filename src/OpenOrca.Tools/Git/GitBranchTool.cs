using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Git;

public sealed class GitBranchTool : IOrcaTool
{
    public string Name => "git_branch";
    public string Description => "List, create, or delete git branches. Lists both local and remote branches. Use git_checkout to switch branches after creating.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["list", "create", "delete"],
                "description": "Action to perform. Defaults to 'list'."
            },
            "name": {
                "type": "string",
                "description": "Branch name (required for create/delete)"
            },
            "path": {
                "type": "string",
                "description": "The repository path. Defaults to current directory."
            }
        }
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var action = args.TryGetProperty("action", out var a) ? a.GetString() ?? "list" : "list";
        var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";

        return action switch
        {
            "create" when !string.IsNullOrEmpty(name) =>
                await GitHelper.RunGitAsync($"branch {GitHelper.EscapeArg(name)}", path, ct),
            "delete" when !string.IsNullOrEmpty(name) =>
                await GitHelper.RunGitAsync($"branch -d {GitHelper.EscapeArg(name)}", path, ct),
            "list" =>
                await GitHelper.RunGitAsync("branch -a", path, ct),
            _ =>
                ToolResult.Error("Invalid action or missing branch name.")
        };
    }
}
