using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Git;

public sealed class GitStashTool : IOrcaTool
{
    public string Name => "git_stash";
    public string Description => "Stash or restore uncommitted changes. Actions: push (save changes), pop (restore and remove), list (show stashes), apply (restore and keep), drop (discard a stash).";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["push", "pop", "list", "apply", "drop"],
                "description": "Stash action to perform. Defaults to 'push'."
            },
            "message": {
                "type": "string",
                "description": "Message for 'push' action to label the stash."
            },
            "index": {
                "type": "integer",
                "description": "Stash index for pop/apply/drop (e.g. 0 for stash@{0}). Defaults to 0."
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
        var action = args.TryGetProperty("action", out var a) ? a.GetString() ?? "push" : "push";
        var message = args.TryGetProperty("message", out var m) ? m.GetString() : null;
        var index = args.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";

        var gitArgs = action switch
        {
            "push" when !string.IsNullOrEmpty(message) => $"stash push -m \"{message.Replace("\"", "\\\"")}\"",
            "push" => "stash push",
            "pop" => $"stash pop stash@{{{index}}}",
            "list" => "stash list",
            "apply" => $"stash apply stash@{{{index}}}",
            "drop" => $"stash drop stash@{{{index}}}",
            _ => $"stash {action}"
        };

        return await GitHelper.RunGitAsync(gitArgs, path, ct);
    }
}
