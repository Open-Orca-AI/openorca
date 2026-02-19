using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.FileSystem;

public sealed class CdTool : IOrcaTool
{
    public string Name => "cd";
    public string Description => "Change the current working directory for all subsequent tool calls. Returns the new absolute path. Affects bash, read_file, glob, grep, and all path-relative operations.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "The path to change to (absolute or relative to the current working directory)"
            }
        },
        "required": ["path"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var destination = args.GetProperty("path").GetString()!;
        var targetPath = Path.GetFullPath(destination);

        try
        {
            if (!Directory.Exists(targetPath))
                return Task.FromResult(ToolResult.Error($"Directory does not exist: {targetPath}"));

            Directory.SetCurrentDirectory(targetPath);
            return Task.FromResult(ToolResult.Success($"Changed directory to: {targetPath}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error changing directory: {ex.Message}"));
        }
    }
}
