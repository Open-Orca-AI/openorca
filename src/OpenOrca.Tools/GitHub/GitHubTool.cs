using System.Diagnostics;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.GitHub;

public sealed class GitHubTool : IOrcaTool
{
    public string Name => "github";
    public string Description =>
        "Interact with GitHub using the gh CLI. Actions: " +
        "pr_list (list pull requests), pr_view (view PR details), pr_create (create PR), pr_diff (view PR diff), " +
        "pr_merge (merge PR), pr_checks (view PR check status), pr_comment (comment on PR), " +
        "issue_list (list issues), issue_view (view issue), issue_create (create issue), " +
        "repo_view (view repo info). Requires the 'gh' CLI to be installed and authenticated.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": [
                    "pr_list", "pr_view", "pr_create", "pr_diff", "pr_merge", "pr_checks", "pr_comment",
                    "issue_list", "issue_view", "issue_create",
                    "repo_view"
                ],
                "description": "The GitHub action to perform"
            },
            "number": {
                "type": "integer",
                "description": "PR or issue number (required for pr_view, pr_diff, pr_merge, pr_checks, pr_comment, issue_view)"
            },
            "title": {
                "type": "string",
                "description": "Title for pr_create or issue_create"
            },
            "body": {
                "type": "string",
                "description": "Body/description for pr_create, issue_create, or pr_comment"
            },
            "base": {
                "type": "string",
                "description": "Base branch for pr_create (e.g. 'main'). Defaults to repo default branch."
            },
            "head": {
                "type": "string",
                "description": "Head branch for pr_create. Defaults to current branch."
            },
            "state": {
                "type": "string",
                "enum": ["open", "closed", "merged", "all"],
                "description": "Filter by state for pr_list or issue_list. Defaults to 'open'."
            },
            "labels": {
                "type": "string",
                "description": "Comma-separated labels for issue_create (e.g. 'bug,urgent')"
            },
            "merge_method": {
                "type": "string",
                "enum": ["merge", "squash", "rebase"],
                "description": "Merge method for pr_merge. Defaults to 'merge'."
            },
            "limit": {
                "type": "integer",
                "description": "Max results for pr_list and issue_list. Defaults to 20."
            },
            "repo": {
                "type": "string",
                "description": "Repository in 'owner/repo' format. Defaults to the repo in the current directory."
            },
            "path": {
                "type": "string",
                "description": "Working directory for the command. Defaults to current directory."
            }
        },
        "required": ["action"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var action = args.GetProperty("action").GetString()!;
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";

        var ghArgs = BuildGhArgs(action, args);
        if (ghArgs is null)
            return ToolResult.Error($"Invalid action or missing required parameters for '{action}'.");

        return await GhHelper.RunGhAsync(ghArgs, path, ct);
    }

    private static string? BuildGhArgs(string action, JsonElement args)
    {
        var number = args.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
        var title = args.TryGetProperty("title", out var t) ? t.GetString() : null;
        var body = args.TryGetProperty("body", out var b) ? b.GetString() : null;
        var baseBranch = args.TryGetProperty("base", out var bb) ? bb.GetString() : null;
        var head = args.TryGetProperty("head", out var h) ? h.GetString() : null;
        var state = args.TryGetProperty("state", out var s) ? s.GetString() ?? "open" : "open";
        var labels = args.TryGetProperty("labels", out var l) ? l.GetString() : null;
        var mergeMethod = args.TryGetProperty("merge_method", out var mm) ? mm.GetString() ?? "merge" : "merge";
        var limit = args.TryGetProperty("limit", out var lm) ? lm.GetInt32() : 20;
        var repo = args.TryGetProperty("repo", out var r) ? r.GetString() : null;

        var repoFlag = !string.IsNullOrEmpty(repo) ? $" --repo {repo}" : "";

        return action switch
        {
            "pr_list" =>
                $"pr list --state {state} --limit {limit}{repoFlag}",

            "pr_view" when number > 0 =>
                $"pr view {number}{repoFlag}",

            "pr_create" when !string.IsNullOrEmpty(title) =>
                BuildPrCreate(title, body, baseBranch, head, repoFlag),

            "pr_diff" when number > 0 =>
                $"pr diff {number}{repoFlag}",

            "pr_merge" when number > 0 =>
                $"pr merge {number} --{mergeMethod}{repoFlag}",

            "pr_checks" when number > 0 =>
                $"pr checks {number}{repoFlag}",

            "pr_comment" when number > 0 && !string.IsNullOrEmpty(body) =>
                $"pr comment {number} --body \"{EscapeArg(body)}\"{repoFlag}",

            "issue_list" =>
                $"issue list --state {state} --limit {limit}{repoFlag}",

            "issue_view" when number > 0 =>
                $"issue view {number}{repoFlag}",

            "issue_create" when !string.IsNullOrEmpty(title) =>
                BuildIssueCreate(title, body, labels, repoFlag),

            "repo_view" =>
                $"repo view{repoFlag}",

            _ => null
        };
    }

    private static string BuildPrCreate(string title, string? body, string? baseBranch, string? head, string repoFlag)
    {
        var cmd = $"pr create --title \"{EscapeArg(title)}\"";
        if (!string.IsNullOrEmpty(body))
            cmd += $" --body \"{EscapeArg(body)}\"";
        if (!string.IsNullOrEmpty(baseBranch))
            cmd += $" --base {baseBranch}";
        if (!string.IsNullOrEmpty(head))
            cmd += $" --head {head}";
        cmd += repoFlag;
        return cmd;
    }

    private static string BuildIssueCreate(string title, string? body, string? labels, string repoFlag)
    {
        var cmd = $"issue create --title \"{EscapeArg(title)}\"";
        if (!string.IsNullOrEmpty(body))
            cmd += $" --body \"{EscapeArg(body)}\"";
        if (!string.IsNullOrEmpty(labels))
            cmd += $" --label \"{EscapeArg(labels)}\"";
        cmd += repoFlag;
        return cmd;
    }

    private static string EscapeArg(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

/// <summary>
/// Helper to run gh CLI commands, similar to GitHelper for git commands.
/// Does NOT require a .git repo — some gh commands work without one (e.g. repo view --repo owner/name).
/// </summary>
internal static class GhHelper
{
    public static async Task<ToolResult> RunGhAsync(string arguments, string workingDirectory, CancellationToken ct)
    {
        workingDirectory = Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(workingDirectory))
            return ToolResult.Error($"Directory not found: {workingDirectory}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);

            if (process is null)
                return ToolResult.Error(
                    "Failed to start 'gh' CLI. Is it installed? " +
                    "Install from https://cli.github.com/ and run 'gh auth login' to authenticate.");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort process kill */ }
                return ToolResult.Error("GitHub CLI command timed out after 30 seconds.");
            }

            if (process.ExitCode != 0)
            {
                var errMsg = string.IsNullOrEmpty(error) ? output : error;

                // Provide helpful hints for common errors
                if (errMsg.Contains("gh auth login") || errMsg.Contains("not logged"))
                    return ToolResult.Error(
                        $"GitHub CLI not authenticated. Run 'gh auth login' first.\n\n{errMsg}");

                if (errMsg.Contains("Could not resolve") || errMsg.Contains("not a git repository"))
                    return ToolResult.Error(
                        $"Not in a GitHub repository. Use --repo owner/name or cd into a git repo.\n\n{errMsg}");

                return ToolResult.Error(errMsg);
            }

            return ToolResult.Success(string.IsNullOrEmpty(output) ? "(no output)" : output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ToolResult.Error(
                "The 'gh' CLI is not installed or not in PATH. " +
                "Install from https://cli.github.com/ and run 'gh auth login' to authenticate.");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"GitHub CLI error: {ex.Message}");
        }
    }
}
