using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.FileSystem;

public sealed class DeleteFileTool : IOrcaTool
{
    public string Name => "delete_file";
    public string Description => "Delete a file or directory. Requires recursive=true to delete non-empty directories. Rejects dangerous system paths for safety.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "The path of the file or directory to delete"
            },
            "recursive": {
                "type": "boolean",
                "description": "Required to delete non-empty directories. Defaults to false."
            }
        },
        "required": ["path"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.GetProperty("path").GetString()!;
        var recursive = args.TryGetProperty("recursive", out var r) && r.GetBoolean();

        path = Path.GetFullPath(path);

        if (PathSafetyHelper.IsDangerousPath(path))
            return Task.FromResult(ToolResult.Error($"Refusing to delete dangerous path: {path}"));

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return Task.FromResult(ToolResult.Success($"Deleted file: {path}"));
            }

            if (Directory.Exists(path))
            {
                if (!recursive && Directory.EnumerateFileSystemEntries(path).Any())
                    return Task.FromResult(ToolResult.Error(
                        $"Directory is not empty: {path}. Set recursive=true to delete non-empty directories."));

                Directory.Delete(path, recursive);
                return Task.FromResult(ToolResult.Success($"Deleted directory: {path}"));
            }

            return Task.FromResult(ToolResult.Error($"Path not found: {path}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error deleting: {ex.Message}"));
        }
    }
}
