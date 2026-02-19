using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Git;

public sealed class GitLogTool : IOrcaTool
{
    public string Name => "git_log";
    public string Description => "Show recent commit history with one-line summaries. Filter by file path or author name.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "The repository path. Defaults to current directory."
            },
            "count": {
                "type": "integer",
                "description": "Number of commits to show. Defaults to 20."
            },
            "oneline": {
                "type": "boolean",
                "description": "Use one-line format. Defaults to true."
            },
            "file": {
                "type": "string",
                "description": "Filter commits to those affecting this file path."
            },
            "author": {
                "type": "string",
                "description": "Filter commits by author name or email."
            }
        }
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";
        var count = args.TryGetProperty("count", out var c) ? c.GetInt32() : 20;
        var oneline = !args.TryGetProperty("oneline", out var o) || o.GetBoolean();
        var file = args.TryGetProperty("file", out var f) ? f.GetString() : null;
        var author = args.TryGetProperty("author", out var a) ? a.GetString() : null;

        var format = oneline ? "--oneline" : "--format=medium";
        var gitArgs = $"log {format} -n {count}";

        if (!string.IsNullOrEmpty(author))
            gitArgs += $" --author=\"{author}\"";

        if (!string.IsNullOrEmpty(file))
            gitArgs += $" -- {file}";

        return await GitHelper.RunGitAsync(gitArgs, path, ct);
    }
}
