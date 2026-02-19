using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.FileSystem;

public sealed class MoveFileTool : IOrcaTool
{
    public string Name => "move_file";
    public string Description => "Move or rename a file or directory. Creates parent directories for the destination automatically. Set overwrite=true to replace an existing destination.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "source": {
                "type": "string",
                "description": "The source file or directory path"
            },
            "destination": {
                "type": "string",
                "description": "The destination path"
            },
            "overwrite": {
                "type": "boolean",
                "description": "Overwrite the destination if it exists. Defaults to false."
            }
        },
        "required": ["source", "destination"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var source = Path.GetFullPath(args.GetProperty("source").GetString()!);
        var destination = Path.GetFullPath(args.GetProperty("destination").GetString()!);
        var overwrite = args.TryGetProperty("overwrite", out var ow) && ow.GetBoolean();

        if (PathSafetyHelper.IsDangerousPath(source))
            return Task.FromResult(ToolResult.Error($"Refusing to move dangerous path: {source}"));

        try
        {
            // Ensure parent directory exists
            var destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite);
                return Task.FromResult(ToolResult.Success($"Moved file: {source} → {destination}"));
            }

            if (Directory.Exists(source))
            {
                if (Directory.Exists(destination))
                {
                    if (!overwrite)
                        return Task.FromResult(ToolResult.Error(
                            $"Destination directory already exists: {destination}. Set overwrite=true or choose a different name."));

                    // Move destination to temp backup first to prevent data loss if move fails
                    var backupPath = destination + ".orca-backup-" + Guid.NewGuid().ToString("N")[..8];
                    Directory.Move(destination, backupPath);
                    try
                    {
                        Directory.Move(source, destination);
                        Directory.Delete(backupPath, recursive: true);
                    }
                    catch
                    {
                        // Restore backup on failure
                        if (Directory.Exists(backupPath) && !Directory.Exists(destination))
                            Directory.Move(backupPath, destination);
                        throw;
                    }
                }
                else
                {
                    Directory.Move(source, destination);
                }

                return Task.FromResult(ToolResult.Success($"Moved directory: {source} → {destination}"));
            }

            return Task.FromResult(ToolResult.Error($"Source not found: {source}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error moving: {ex.Message}"));
        }
    }
}
