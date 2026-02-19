using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Git;

public sealed class GitDiffTool : IOrcaTool
{
    public string Name => "git_diff";
    public string Description => "Show changes between commits, working tree, and staging area. Supports staged diffs (--cached), file-specific diffs, and branch comparisons via the base parameter.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "The repository path. Defaults to current directory."
            },
            "staged": {
                "type": "boolean",
                "description": "Show staged changes (--cached). Defaults to false."
            },
            "file": {
                "type": "string",
                "description": "Optional specific file to diff."
            },
            "base": {
                "type": "string",
                "description": "Diff against a branch or commit (e.g. 'main', 'HEAD~3'). Uses three-dot diff: base...HEAD."
            }
        }
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";
        var staged = args.TryGetProperty("staged", out var s) && s.GetBoolean();
        var file = args.TryGetProperty("file", out var f) ? f.GetString() : null;
        var baseBranch = args.TryGetProperty("base", out var b) ? b.GetString() : null;

        string gitArgs;
        if (!string.IsNullOrEmpty(baseBranch))
        {
            gitArgs = $"diff {GitHelper.EscapeArg(baseBranch + "...HEAD")}";
        }
        else
        {
            gitArgs = "diff";
            if (staged) gitArgs += " --cached";
        }

        if (!string.IsNullOrEmpty(file)) gitArgs += $" -- {GitHelper.EscapeArg(file)}";

        return await GitHelper.RunGitAsync(gitArgs, path, ct);
    }
}
