using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.Shell;

public sealed class BashTool : IOrcaTool, IStreamingOrcaTool
{
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Default timeout in seconds. Set from config (Shell.IdleTimeoutSeconds).
    /// Used as the default for timeout_seconds when not specified per-call.
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 15;

    public string Name => "bash";
    public string Description => "Execute a shell command and return its output. Uses cmd.exe on Windows and /bin/bash on Unix. Output streams to the user in real-time. Waits up to timeout_seconds (default 15s) for completion — if the command is still running, returns the process ID so you can check on it with get_process_output or stop it with stop_process. Output is truncated at 30,000 chars.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "command": {
                "type": "string",
                "description": "The shell command to execute"
            },
            "working_directory": {
                "type": "string",
                "description": "Working directory for the command. Defaults to current directory."
            },
            "timeout_seconds": {
                "type": "integer",
                "description": "Max seconds to wait for the command to complete (default 15). If exceeded, the process continues in background and its ID is returned."
            },
            "description": {
                "type": "string",
                "description": "Human-readable description of what this command does (for logging/display only)"
            }
        },
        "required": ["command"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        return ExecuteStreamingAsync(args, _ => { }, ct);
    }

    public async Task<ToolResult> ExecuteStreamingAsync(
        JsonElement args, Action<string> onOutput, CancellationToken ct)
    {
        var command = args.GetProperty("command").GetString()!;
        var workDir = args.TryGetProperty("working_directory", out var wd) ? wd.GetString() ?? "." : ".";
        var timeoutSec = args.TryGetProperty("timeout_seconds", out var ts) ? ts.GetInt32Lenient(IdleTimeoutSeconds) : IdleTimeoutSeconds;

        workDir = Path.GetFullPath(workDir);
        Logger?.LogDebug("Executing bash: {Command} in {WorkDir} (timeout: {Timeout}s)",
            command.Length > 200 ? command[..200] + "..." : command, workDir, timeoutSec);

        if (!Directory.Exists(workDir))
            return ToolResult.Error($"Working directory not found: {workDir}");

        try
        {
            var managed = BackgroundProcessManager.Start(command, workDir);
            // Use array to allow mutation from async methods (ref not allowed in async)
            var cursor = new int[] { 0 };

            // Poll for new output lines, streaming to user in real-time
            var exited = await PollOutputAsync(managed, cursor, onOutput,
                TimeSpan.FromSeconds(timeoutSec), ct);

            if (ct.IsCancellationRequested)
                return ToolResult.Error("Command cancelled by user.");

            if (exited)
            {
                // Small delay to let final output flush from async readers
                await Task.Delay(150, CancellationToken.None);
                FlushNewLines(managed, cursor, onOutput);

                return FormatCompletedResult(managed, cursor[0]);
            }
            else
            {
                return FormatTimeoutResult(managed, cursor[0], timeoutSec);
            }
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Error("Command cancelled by user.");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to execute command: {ex.Message}");
        }
    }

    /// <summary>
    /// Poll for new output lines until the process exits or the timeout elapses.
    /// Returns true if the process exited within the timeout.
    /// </summary>
    private async Task<bool> PollOutputAsync(
        ManagedProcess managed, int[] cursor, Action<string> onOutput,
        TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            FlushNewLines(managed, cursor, onOutput);

            if (managed.HasExited)
                return true;

            // Wait a short interval, but also check if process exits sooner
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            var waitTime = TimeSpan.FromMilliseconds(Math.Min(100, remaining.TotalMilliseconds));
            var exitedDuringWait = await managed.WaitForExitAsync(waitTime, ct);
            if (exitedDuringWait)
            {
                FlushNewLines(managed, cursor, onOutput);
                return true;
            }
        }

        // Final flush before returning timeout
        FlushNewLines(managed, cursor, onOutput);
        return false;
    }

    private static void FlushNewLines(ManagedProcess managed, int[] cursor, Action<string> onOutput)
    {
        var (lines, newCursor) = managed.GetNewLines(cursor[0]);
        cursor[0] = newCursor;
        foreach (var line in lines)
            onOutput(line);
    }

    private static ToolResult FormatCompletedResult(ManagedProcess managed, int cursor)
    {
        var lines = managed.GetTailLines(cursor);
        var output = string.Join("\n", lines);

        const int maxLen = 30000;
        if (output.Length > maxLen)
            output = output[..maxLen] + $"\n... ({output.Length - maxLen} chars truncated)";

        var exitCode = managed.ExitCode ?? 0;
        var header = $"Working directory: {managed.WorkingDirectory}\nExit code: {exitCode}\n\n";
        return exitCode == 0
            ? ToolResult.Success(header + output)
            : ToolResult.Error(header + output);
    }

    private static ToolResult FormatTimeoutResult(ManagedProcess managed, int cursor, int timeoutSec)
    {
        var lines = managed.GetTailLines(cursor);
        var output = string.Join("\n", lines);

        const int maxLen = 30000;
        if (output.Length > maxLen)
            output = output[..maxLen] + $"\n... ({output.Length - maxLen} chars truncated)";

        var sb = new StringBuilder();
        sb.AppendLine($"Command is still running in the background (process ID: \"{managed.Id}\").");
        sb.AppendLine($"Working directory: {managed.WorkingDirectory}");
        sb.AppendLine($"Elapsed: {timeoutSec}s");
        sb.AppendLine();
        if (output.Length > 0)
        {
            sb.AppendLine("--- Output so far ---");
            sb.AppendLine(output);
            sb.AppendLine();
        }
        sb.AppendLine($"The process is still running. Use get_process_output with process_id \"{managed.Id}\" to check for new output.");
        sb.Append($"Use stop_process with process_id \"{managed.Id}\" to terminate it.");

        return ToolResult.Success(sb.ToString());
    }

}
