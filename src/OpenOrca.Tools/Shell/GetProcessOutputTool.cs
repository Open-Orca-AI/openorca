using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Shell;

public sealed class GetProcessOutputTool : IOrcaTool
{
    public string Name => "get_process_output";
    public string Description => "Get recent output from a background process started with start_background_process. Returns the last N lines of stdout/stderr, process status, and uptime.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "process_id": {
                "type": "string",
                "description": "The process ID returned by start_background_process"
            },
            "tail_lines": {
                "type": "integer",
                "description": "Number of recent output lines to return. Defaults to 50."
            }
        },
        "required": ["process_id"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var processId = args.GetProperty("process_id").GetString()!;
        var tailLines = args.TryGetProperty("tail_lines", out var tl) ? tl.GetInt32() : 50;

        // Resolve "last"/"latest" to the most recently started process
        if (processId.Equals("last", StringComparison.OrdinalIgnoreCase) ||
            processId.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            var latest = BackgroundProcessManager.ListAll()
                .OrderByDescending(p => p.StartTimeUtc).FirstOrDefault();
            if (latest is null)
                return Task.FromResult(ToolResult.Error("No background processes found."));
            processId = latest.Id;
        }

        var managed = BackgroundProcessManager.Get(processId);
        if (managed is null)
            return Task.FromResult(ToolResult.Error($"No process found with ID \"{processId}\". Use start_background_process first."));

        var lines = managed.GetTailLines(tailLines);
        var status = managed.HasExited
            ? $"Exited with code {managed.ExitCode}"
            : "Running";
        var uptime = DateTime.UtcNow - managed.StartTimeUtc;

        var sb = new StringBuilder();
        sb.AppendLine($"Process {managed.Id}: {managed.Command}");
        sb.AppendLine($"Status: {status}");
        sb.AppendLine($"Uptime: {uptime:hh\\:mm\\:ss}");
        sb.AppendLine($"Total lines captured: {managed.TotalLinesCaptured}");
        sb.AppendLine($"--- Last {lines.Count} lines ---");
        foreach (var line in lines)
            sb.AppendLine(line);

        return Task.FromResult(ToolResult.Success(sb.ToString()));
    }
}
