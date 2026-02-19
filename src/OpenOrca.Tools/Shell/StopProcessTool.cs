using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Shell;

public sealed class StopProcessTool : IOrcaTool
{
    public string Name => "stop_process";
    public string Description => "Stop a background process by its ID. Kills the entire process tree and returns the final output. Use this to clean up servers and watchers when done.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "process_id": {
                "type": "string",
                "description": "The process ID returned by start_background_process"
            }
        },
        "required": ["process_id"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var processId = args.GetProperty("process_id").GetString()!;

        var managed = BackgroundProcessManager.Get(processId);
        if (managed is null)
            return ToolResult.Error($"No process found with ID \"{processId}\".");

        var wasRunning = !managed.HasExited;
        BackgroundProcessManager.Stop(processId);

        // Small delay to allow final output to flush
        await Task.Delay(200, ct);

        var lines = managed.GetTailLines(20);

        var sb = new StringBuilder();
        sb.AppendLine(wasRunning
            ? $"Stopped process {managed.Id}: {managed.Command}"
            : $"Process {managed.Id} had already exited (code {managed.ExitCode}): {managed.Command}");
        sb.AppendLine($"--- Last {lines.Count} lines ---");
        foreach (var line in lines)
            sb.AppendLine(line);

        return ToolResult.Success(sb.ToString());
    }
}
