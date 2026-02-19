using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenOrca.Core.Configuration;

namespace OpenOrca.Core.Hooks;

public sealed class HookRunner
{
    private readonly HooksConfig _config;
    private readonly ILogger<HookRunner> _logger;

    public HookRunner(HooksConfig config, ILogger<HookRunner> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Run a pre-tool hook. Returns true if the tool should proceed, false if blocked.
    /// </summary>
    public async Task<bool> RunPreHookAsync(string toolName, string argsJson, CancellationToken ct)
    {
        var command = FindHook(_config.PreToolHooks, toolName);
        if (command is null) return true;

        _logger.LogDebug("Running pre-hook for {Tool}: {Command}", toolName, command);

        var exitCode = await RunHookProcessAsync(command, toolName, argsJson, null, false, ct);
        if (exitCode != 0)
        {
            _logger.LogWarning("Pre-hook for {Tool} returned exit code {ExitCode} — blocking tool", toolName, exitCode);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Run a post-tool hook (fire and forget — non-zero exit is logged but doesn't affect anything).
    /// </summary>
    public async Task RunPostHookAsync(string toolName, string argsJson, string result, bool isError, CancellationToken ct)
    {
        var command = FindHook(_config.PostToolHooks, toolName);
        if (command is null) return;

        _logger.LogDebug("Running post-hook for {Tool}: {Command}", toolName, command);

        try
        {
            await RunHookProcessAsync(command, toolName, argsJson, result, isError, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Post-hook for {Tool} failed", toolName);
        }
    }

    private string? FindHook(Dictionary<string, string> hooks, string toolName)
    {
        // Check specific tool name first
        if (hooks.TryGetValue(toolName, out var command))
            return command;

        // Check wildcard
        if (hooks.TryGetValue("*", out var wildcardCommand))
            return wildcardCommand;

        return null;
    }

    private async Task<int> RunHookProcessAsync(
        string command, string toolName, string argsJson,
        string? result, bool isError, CancellationToken ct)
    {
        string shell;
        string shellArgs;

        if (OperatingSystem.IsWindows())
        {
            shell = "cmd.exe";
            shellArgs = $"/c {command}";
        }
        else
        {
            shell = "/bin/bash";
            shellArgs = $"-c \"{command.Replace("\"", "\\\"")}\"";
        }

        var psi = new ProcessStartInfo(shell, shellArgs)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment["ORCA_TOOL_NAME"] = toolName;
        psi.Environment["ORCA_TOOL_ARGS"] = argsJson;
        if (result is not null)
            psi.Environment["ORCA_TOOL_RESULT"] = result.Length > 10000 ? result[..10000] : result;
        psi.Environment["ORCA_TOOL_ERROR"] = isError.ToString();

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            _logger.LogWarning("Failed to start hook process: {Command}", command);
            return -1;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await proc.WaitForExitAsync(timeout.Token);
            return proc.ExitCode;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Hook timed out (30s): {Command}", command);
            try { proc.Kill(); } catch { }
            return -1;
        }
    }
}
