using System.Diagnostics;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Git;

public sealed class GitStatusTool : IOrcaTool
{
    public string Name => "git_status";
    public string Description => "Show the working tree status (short format with branch info). Returns modified, staged, and untracked files. Always run this before git_commit to see what will be committed.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "The repository path. Defaults to current directory."
            }
        }
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";
        return await GitHelper.RunGitAsync("status --short --branch", path, ct);
    }
}

internal static class GitHelper
{
    /// <summary>
    /// Escapes a value for safe interpolation into a git argument string.
    /// Wraps in double quotes with backslash-escaped inner quotes and backslashes.
    /// </summary>
    public static string EscapeArg(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    private const int GitTimeoutSeconds = 60;

    public static async Task<ToolResult> RunGitAsync(string arguments, string workingDirectory, CancellationToken ct)
    {
        workingDirectory = Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(workingDirectory))
            return ToolResult.Error($"Directory not found: {workingDirectory}");

        if (!IsGitRepository(workingDirectory))
            return ToolResult.Error(
                $"Not a git repository: {workingDirectory}. " +
                "No .git directory was found here or in any parent directory. " +
                "To fix this, use the bash tool to run 'git init' in the target directory, " +
                "or change the 'path' parameter to point to an existing git repository.");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(GitTimeoutSeconds));

            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            if (process.ExitCode != 0)
                return ToolResult.Error(string.IsNullOrEmpty(error) ? output : error);

            return ToolResult.Success(string.IsNullOrEmpty(output) ? "(no output)" : output);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolResult.Error($"Git command timed out after {GitTimeoutSeconds}s: git {arguments}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Git error: {ex.Message}");
        }
    }

    private static bool IsGitRepository(string directory)
    {
        var dir = new DirectoryInfo(directory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
                return true;
            dir = dir.Parent;
        }
        return false;
    }
}
