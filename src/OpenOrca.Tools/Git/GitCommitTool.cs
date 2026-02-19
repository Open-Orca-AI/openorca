using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Git;

public sealed class GitCommitTool : IOrcaTool
{
    public string Name => "git_commit";
    public string Description => "Stage files and create a git commit. Can stage specific files or all changes. Returns the commit hash and a diff stat summary after committing.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "message": {
                "type": "string",
                "description": "The commit message"
            },
            "files": {
                "type": "array",
                "items": { "type": "string" },
                "description": "Specific files to stage. If empty, stages all changes."
            },
            "path": {
                "type": "string",
                "description": "The repository path. Defaults to current directory."
            }
        },
        "required": ["message"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var message = args.GetProperty("message").GetString()!;
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";

        var files = new List<string>();
        if (args.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in f.EnumerateArray())
            {
                var val = file.GetString();
                if (val is not null) files.Add(val);
            }
        }

        // Stage files
        var stageArgs = files.Count > 0
            ? $"add {string.Join(" ", files.Select(f => $"\"{f}\""))}"
            : "add -A";

        var stageResult = await GitHelper.RunGitAsync(stageArgs, path, ct);
        if (stageResult.IsError)
            return ToolResult.Error($"Failed to stage: {stageResult.Content}");

        // Commit
        var escapedMessage = message.Replace("\"", "\\\"");
        var commitResult = await GitHelper.RunGitAsync($"commit -m \"{escapedMessage}\"", path, ct);
        if (commitResult.IsError)
            return commitResult;

        // Get commit summary
        var sb = new StringBuilder();
        sb.AppendLine(commitResult.Content);

        var logResult = await GitHelper.RunGitAsync("log --oneline -1", path, ct);
        if (!logResult.IsError)
        {
            sb.AppendLine();
            sb.AppendLine($"Commit: {logResult.Content.Trim()}");
        }

        var diffStatResult = await GitHelper.RunGitAsync("diff --stat HEAD~1", path, ct);
        if (!diffStatResult.IsError)
        {
            sb.AppendLine();
            sb.AppendLine(diffStatResult.Content);
        }

        return ToolResult.Success(sb.ToString());
    }
}
