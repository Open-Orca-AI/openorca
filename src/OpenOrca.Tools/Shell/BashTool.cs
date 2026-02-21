using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Shell;

public sealed class BashTool : IOrcaTool
{
    public ILogger? Logger { get; set; }

    public string Name => "bash";
    public string Description => "Execute a shell command and return its output. Uses cmd.exe on Windows and /bin/bash on Unix. Supports working directory and timeout (default 120s). Output is truncated at 30,000 chars. Use for builds, tests, scripts, installs, and short-lived system commands. Do NOT use for servers, watchers, REPLs, or interactive programs — use start_background_process instead.";
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
        var timeoutSec = args.TryGetProperty("timeout_seconds", out var ts) ? ts.GetInt32() : 120;

        workDir = Path.GetFullPath(workDir);
        Logger?.LogDebug("Executing bash: {Command} in {WorkDir} (timeout: {Timeout}s)",
            command.Length > 200 ? command[..200] + "..." : command, workDir, timeoutSec);

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

            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

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

            var output = await outputTask;
            var error = await errorTask;

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
}
