using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Shell;

public sealed class BashTool : IOrcaTool
{
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Default idle timeout in seconds. Set from config (Shell.IdleTimeoutSeconds).
    /// 0 = disabled. Can be overridden per-call via idle_timeout_seconds parameter.
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 15;

    public string Name => "bash";
    public string Description => "Execute a shell command and return its output. Uses cmd.exe on Windows and /bin/bash on Unix. Supports working directory and timeout (default 120s). Output is truncated at 30,000 chars. If no stdout is produced within the idle timeout (default 15s), the command is aborted with a suggestion to use start_background_process. Set idle_timeout_seconds to 0 to disable for slow-starting commands. Do NOT use for servers, watchers, REPLs, or interactive programs — use start_background_process instead.";
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
                "description": "Timeout in seconds. Defaults to 120."
            },
            "idle_timeout_seconds": {
                "type": "integer",
                "description": "Abort if no stdout for this many seconds (default 15). Set to 0 to disable for slow-starting commands."
            },
            "description": {
                "type": "string",
                "description": "Human-readable description of what this command does (for logging/display only)"
            }
        },
        "required": ["command"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var command = args.GetProperty("command").GetString()!;
        var workDir = args.TryGetProperty("working_directory", out var wd) ? wd.GetString() ?? "." : ".";
        var timeoutSec = args.TryGetProperty("timeout_seconds", out var ts) ? GetIntTolerant(ts, 120) : 120;
        var idleTimeoutSec = args.TryGetProperty("idle_timeout_seconds", out var its) ? GetIntTolerant(its, IdleTimeoutSeconds) : IdleTimeoutSeconds;

        workDir = Path.GetFullPath(workDir);
        Logger?.LogDebug("Executing bash: {Command} in {WorkDir} (timeout: {Timeout}s, idle: {Idle}s)",
            command.Length > 200 ? command[..200] + "..." : command, workDir, timeoutSec, idleTimeoutSec);

        if (!Directory.Exists(workDir))
            return ToolResult.Error($"Working directory not found: {workDir}");

        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var psi = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/bash",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;

            // Write command via stdin to avoid shell argument escaping issues
            if (isWindows)
                await process.StandardInput.WriteLineAsync("@echo off");
            await process.StandardInput.WriteLineAsync(command);
            process.StandardInput.Close();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            var idleTimedOut = false;

            // Read stderr in a parallel task (does NOT reset idle timer)
            var stderrTask = Task.Run(async () =>
            {
                var buffer = new char[4096];
                try
                {
                    int read;
                    while ((read = await process.StandardError.ReadAsync(buffer, timeoutCts.Token)) > 0)
                        stderrBuilder.Append(buffer, 0, read);
                }
                catch (OperationCanceledException) { }
            }, timeoutCts.Token);

            // Read stdout with idle timeout tracking
            try
            {
                var buffer = new char[4096];
                var useIdleTimeout = idleTimeoutSec > 0;

                while (true)
                {
                    CancellationToken readToken;
                    CancellationTokenSource? idleCts = null;

                    if (useIdleTimeout)
                    {
                        idleCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
                        idleCts.CancelAfter(TimeSpan.FromSeconds(idleTimeoutSec));
                        readToken = idleCts.Token;
                    }
                    else
                    {
                        readToken = timeoutCts.Token;
                    }

                    int read;
                    try
                    {
                        read = await process.StandardOutput.ReadAsync(buffer, readToken);
                    }
                    catch (OperationCanceledException)
                    {
                        if (ct.IsCancellationRequested)
                            throw; // user cancellation — rethrow

                        if (timeoutCts.IsCancellationRequested)
                            throw; // total timeout — rethrow

                        // Idle timeout fired
                        idleTimedOut = true;
                        break;
                    }
                    finally
                    {
                        idleCts?.Dispose();
                    }

                    if (read == 0)
                        break; // EOF

                    stdoutBuilder.Append(buffer, 0, read);
                }
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

                if (ct.IsCancellationRequested)
                    return ToolResult.Error("Command cancelled by user.");

                return ToolResult.Error($"Command timed out after {timeoutSec} seconds. " +
                    "If this command runs indefinitely (server, watcher, REPL), use start_background_process instead.");
            }

            if (idleTimedOut)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

                var partial = stdoutBuilder.ToString();
                var msg = $"Command idle timeout: no stdout for {idleTimeoutSec} seconds.\n" +
                    "This typically means the command is waiting for input or running a long-lived process.\n" +
                    "Use start_background_process for servers, watchers, or long-running commands.\n" +
                    "Set idle_timeout_seconds to 0 to disable for slow-starting commands.";

                if (partial.Length > 0)
                    msg += $"\n\nPartial stdout before timeout:\n{partial}";

                return ToolResult.Error(msg);
            }

            // Wait for process exit after stdout EOF
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

                if (ct.IsCancellationRequested)
                    return ToolResult.Error("Command cancelled by user.");

                return ToolResult.Error($"Command timed out after {timeoutSec} seconds. " +
                    "If this command runs indefinitely (server, watcher, REPL), use start_background_process instead.");
            }

            // Wait for stderr to finish
            try { await stderrTask; } catch (OperationCanceledException) { }

            var output = stdoutBuilder.ToString();
            var error = stderrBuilder.ToString();

            const int maxLen = 30000;
            var combined = string.IsNullOrEmpty(error) ? output : $"{output}\n--- stderr ---\n{error}";

            if (combined.Length > maxLen)
                combined = combined[..maxLen] + $"\n... ({combined.Length - maxLen} chars truncated)";

            var header = $"Working directory: {workDir}\nExit code: {process.ExitCode}\n\n";
            return process.ExitCode == 0
                ? ToolResult.Success(header + combined)
                : ToolResult.Error(header + combined);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to execute command: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse an integer from a JsonElement, tolerating string-typed numbers from LLMs.
    /// </summary>
    private static int GetIntTolerant(JsonElement element, int defaultValue)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.GetInt32();
        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
            return parsed;
        return defaultValue;
    }
}
