using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.FileSystem;

public sealed class CreateDirectoryTool : IOrcaTool
{
    public string Name => "create_directory";
    public string Description => "Create a directory and any missing parent directories. Succeeds silently if it already exists.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "The path of the directory to create"
            }
        },
        "required": ["path"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.GetProperty("path").GetString()!;
        path = Path.GetFullPath(path);

        try
        {
            if (Directory.Exists(path))
                return Task.FromResult(ToolResult.Success($"Directory already exists: {path}"));

            Directory.CreateDirectory(path);
            return Task.FromResult(ToolResult.Success($"Directory created: {path}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error creating directory: {ex.Message}"));
        }
    }
}
