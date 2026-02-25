using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.Git;

public sealed class GitPushTool : IOrcaTool
{
    public string Name => "git_push";
    public string Description => "Push commits to a remote repository. Uses --force-with-lease for safe force pushes. Always confirm with the user before force pushing.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;

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
                "description": "Branch to push. Defaults to the current branch."
            },
            "force": {
                "type": "boolean",
                "description": "Force push using --force-with-lease (safer than --force). Defaults to false."
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
        var force = args.TryGetProperty("force", out var f) && f.GetBooleanLenient();
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";

        var gitArgs = $"push {GitHelper.EscapeArg(remote)}";
        if (!string.IsNullOrEmpty(branch))
            gitArgs += $" {GitHelper.EscapeArg(branch)}";
        if (force)
            gitArgs += " --force-with-lease";

        return await GitHelper.RunGitAsync(gitArgs, path, ct);
    }
}
