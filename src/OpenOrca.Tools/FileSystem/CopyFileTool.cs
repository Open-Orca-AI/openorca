using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.FileSystem;

public sealed class CopyFileTool : IOrcaTool
{
    public string Name => "copy_file";
    public string Description => "Copy a file or directory. Set recursive=true to copy directories with contents. Creates parent directories for the destination automatically.";
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
            "recursive": {
                "type": "boolean",
                "description": "Copy directories recursively. Required for directory copies. Defaults to false."
            },
            "overwrite": {
                "type": "boolean",
                "description": "Overwrite existing files at destination. Defaults to false."
            }
        },
        "required": ["source", "destination"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var source = Path.GetFullPath(args.GetProperty("source").GetString()!);
        var destination = Path.GetFullPath(args.GetProperty("destination").GetString()!);
        var recursive = args.TryGetProperty("recursive", out var r) && r.GetBooleanLenient();
        var overwrite = args.TryGetProperty("overwrite", out var ow) && ow.GetBooleanLenient();

        try
        {
            // Ensure parent directory exists
            var destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            if (File.Exists(source))
            {
                await FileRetryHelper.RetryOnIOExceptionAsync(() => File.Copy(source, destination, overwrite), ct);
                return ToolResult.Success($"Copied file: {source} → {destination}");
            }

            if (Directory.Exists(source))
            {
                if (!recursive)
                    return ToolResult.Error(
                        $"Source is a directory. Set recursive=true to copy directories.");

                CopyDirectory(source, destination, overwrite);
                return ToolResult.Success($"Copied directory: {source} → {destination}");
            }

            return ToolResult.Error($"Source not found: {source}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error copying: {ex.Message}");
        }
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        var destRoot = Path.GetFullPath(destination);
        CopyDirectoryCore(source, destination, overwrite, destRoot);
    }

    private static void CopyDirectoryCore(string source, string destination, bool overwrite, string destRoot)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.GetFullPath(Path.Combine(destination, Path.GetFileName(file)));
            if (!destFile.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Path traversal detected: {destFile} escapes destination root.");
            File.Copy(file, destFile, overwrite);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destSubDir = Path.GetFullPath(Path.Combine(destination, Path.GetFileName(dir)));
            if (!destSubDir.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Path traversal detected: {destSubDir} escapes destination root.");
            CopyDirectoryCore(dir, destSubDir, overwrite, destRoot);
        }
    }
}
