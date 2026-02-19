using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.Archive;

public sealed class ArchiveTool : IOrcaTool
{
    public string Name => "archive";
    public string Description => "Create, extract, or list contents of zip archives. Use action 'create' to zip files/directories, 'extract' to unzip, 'list' to view contents.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["create", "extract", "list"],
                "description": "Action: 'create' a zip archive, 'extract' a zip archive, or 'list' its contents"
            },
            "archive_path": {
                "type": "string",
                "description": "Path to the zip archive file"
            },
            "source_path": {
                "type": "string",
                "description": "Path to compress (file or directory). Required for 'create'."
            },
            "output_path": {
                "type": "string",
                "description": "Directory to extract to. Required for 'extract'."
            }
        },
        "required": ["action", "archive_path"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var action = args.GetProperty("action").GetString()!;
        var archivePath = Path.GetFullPath(args.GetProperty("archive_path").GetString()!);

        return action switch
        {
            "create" => await CreateAsync(archivePath, args, ct),
            "extract" => await ExtractAsync(archivePath, args, ct),
            "list" => await Task.FromResult(ListContents(archivePath)),
            _ => ToolResult.Error($"Unknown action: {action}. Use 'create', 'extract', or 'list'.")
        };
    }

    private static Task<ToolResult> CreateAsync(string archivePath, JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("source_path", out var sp))
            return Task.FromResult(ToolResult.Error("'source_path' is required for action 'create'."));

        var sourcePath = Path.GetFullPath(sp.GetString()!);

        if (PathSafetyHelper.IsDangerousPath(sourcePath))
            return Task.FromResult(ToolResult.Error($"Refusing to archive dangerous path: {sourcePath}"));

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            return Task.FromResult(ToolResult.Error($"Source not found: {sourcePath}"));

        try
        {
            var dir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Delete existing archive to overwrite
            if (File.Exists(archivePath))
                File.Delete(archivePath);

            ct.ThrowIfCancellationRequested();

            if (Directory.Exists(sourcePath))
            {
                ZipFile.CreateFromDirectory(sourcePath, archivePath, CompressionLevel.Optimal, includeBaseDirectory: true);
            }
            else
            {
                using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
                archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath), CompressionLevel.Optimal);
            }

            var info = new FileInfo(archivePath);
            return Task.FromResult(ToolResult.Success($"Created: {archivePath} ({info.Length:N0} bytes)"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(ToolResult.Error($"Error creating archive: {ex.Message}"));
        }
    }

    private static Task<ToolResult> ExtractAsync(string archivePath, JsonElement args, CancellationToken ct)
    {
        if (!File.Exists(archivePath))
            return Task.FromResult(ToolResult.Error($"Archive not found: {archivePath}"));

        var outputPath = args.TryGetProperty("output_path", out var op)
            ? Path.GetFullPath(op.GetString()!)
            : Path.GetDirectoryName(archivePath)!;

        if (PathSafetyHelper.IsDangerousPath(outputPath))
            return Task.FromResult(ToolResult.Error($"Refusing to extract to dangerous path: {outputPath}"));

        try
        {
            Directory.CreateDirectory(outputPath);

            ct.ThrowIfCancellationRequested();

            // Validate entries before extracting to prevent zip-slip
            using (var check = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in check.Entries)
                {
                    var destPath = Path.GetFullPath(Path.Combine(outputPath, entry.FullName));
                    if (!destPath.StartsWith(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                        return Task.FromResult(ToolResult.Error($"Archive contains path traversal entry: {entry.FullName}"));
                }
            }

            ZipFile.ExtractToDirectory(archivePath, outputPath, overwriteFiles: true);

            var entryCount = 0;
            using (var archive = ZipFile.OpenRead(archivePath))
                entryCount = archive.Entries.Count;

            return Task.FromResult(ToolResult.Success($"Extracted {entryCount} entries to: {outputPath}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(ToolResult.Error($"Error extracting archive: {ex.Message}"));
        }
    }

    private static ToolResult ListContents(string archivePath)
    {
        if (!File.Exists(archivePath))
            return ToolResult.Error($"Archive not found: {archivePath}");

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var sb = new StringBuilder();
            sb.AppendLine($"Archive: {archivePath}");
            sb.AppendLine($"Entries: {archive.Entries.Count}");
            sb.AppendLine();

            long totalSize = 0;
            var count = 0;

            foreach (var entry in archive.Entries)
            {
                totalSize += entry.Length;
                count++;
                if (count <= 100)
                {
                    sb.AppendLine($"  {entry.FullName,-50} {entry.Length,12:N0} bytes  {entry.LastWriteTime:yyyy-MM-dd HH:mm}");
                }
            }

            if (count > 100)
                sb.AppendLine($"  ... ({count - 100} more entries)");

            sb.AppendLine();
            sb.AppendLine($"Total uncompressed: {totalSize:N0} bytes");

            return ToolResult.Success(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error reading archive: {ex.Message}");
        }
    }
}
