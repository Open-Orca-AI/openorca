using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Shell;

public sealed class ListProcessesTool : IOrcaTool
{
    public string Name => "list_processes";
    public string Description => "List all background processes and their status (running/exited), command, uptime, and process ID.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {}
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var processes = BackgroundProcessManager.ListAll();

        if (processes.Count == 0)
            return Task.FromResult(ToolResult.Success("No background processes."));

        var sb = new StringBuilder();
        sb.AppendLine($"{processes.Count} background process(es):");
        sb.AppendLine();

        foreach (var p in processes)
        {
            var status = p.HasExited ? $"Exited (code {p.ExitCode})" : "Running";
            var uptime = DateTime.UtcNow - p.StartTimeUtc;
            sb.AppendLine($"  ID: {p.Id}");
            sb.AppendLine($"  Command: {p.Command}");
            sb.AppendLine($"  Status: {status}");
            sb.AppendLine($"  Uptime: {uptime:hh\\:mm\\:ss}");
            sb.AppendLine($"  Lines captured: {p.TotalLinesCaptured}");
            sb.AppendLine();
        }

        return Task.FromResult(ToolResult.Success(sb.ToString()));
    }
}
