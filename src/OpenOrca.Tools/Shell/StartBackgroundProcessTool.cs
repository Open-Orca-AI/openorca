using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Shell;

public sealed class StartBackgroundProcessTool : IOrcaTool
{
    public string Name => "start_background_process";
    public string Description => "Start a long-running command in the background (servers, watchers, build --watch). Returns a process ID. Use get_process_output to check output and stop_process to terminate. For short commands, prefer bash instead.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "command": {
                "type": "string",
                "description": "The shell command to execute in the background"
            },
            "working_directory": {
                "type": "string",
                "description": "Working directory for the command. Defaults to current directory."
            }
        },
        "required": ["command"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var command = args.GetProperty("command").GetString()!;
        var workDir = args.TryGetProperty("working_directory", out var wd) ? wd.GetString() ?? "." : ".";

        workDir = Path.GetFullPath(workDir);

        if (!Directory.Exists(workDir))
            return Task.FromResult(ToolResult.Error($"Working directory not found: {workDir}"));

        try
        {
            var managed = BackgroundProcessManager.Start(command, workDir);
            return Task.FromResult(ToolResult.Success(
                $"Started background process {managed.Id}: {command}\nWorking directory: {workDir}\nUse get_process_output with process_id \"{managed.Id}\" to check output."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to start background process: {ex.Message}"));
        }
    }
}
