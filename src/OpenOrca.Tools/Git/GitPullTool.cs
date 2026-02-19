using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Git;

public sealed class GitPullTool : IOrcaTool
{
    public string Name => "git_pull";
    public string Description => "Pull changes from a remote repository. Fetches and merges (or rebases if configured) the specified branch.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "remote": {
                "type": "string",
                "description": "Remote name. Defaults to 'origin'."
            },
            "branch": {
                "type": "string",
                "description": "Branch to pull. Defaults to the tracking branch."
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
        var remote = args.TryGetProperty("remote", out var r) ? r.GetString() ?? "origin" : "origin";
        var branch = args.TryGetProperty("branch", out var b) ? b.GetString() : null;
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";

        var gitArgs = $"pull {remote}";
        if (!string.IsNullOrEmpty(branch))
            gitArgs += $" {branch}";

        return await GitHelper.RunGitAsync(gitArgs, path, ct);
    }
}
