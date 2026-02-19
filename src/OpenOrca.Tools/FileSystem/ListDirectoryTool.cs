using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.FileSystem;

public sealed class ListDirectoryTool : IOrcaTool
{
    public string Name => "list_directory";
    public string Description => "List files and directories in a given path. Returns names with type indicators (/ for directories). Use recursive to see nested structure, show_hidden to include dotfiles, and show_size to see file sizes.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "The directory path to list. Defaults to current directory."
            },
            "recursive": {
                "type": "boolean",
                "description": "List entries recursively (capped at 1000 entries). Defaults to false."
            },
            "show_hidden": {
                "type": "boolean",
                "description": "Include hidden files/directories (starting with '.'). Defaults to false."
            },
            "show_size": {
                "type": "boolean",
                "description": "Show file sizes. Defaults to false."
            }
        }
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";
        var recursive = args.TryGetProperty("recursive", out var r) && r.GetBoolean();
        var showHidden = args.TryGetProperty("show_hidden", out var sh) && sh.GetBoolean();
        var showSize = args.TryGetProperty("show_size", out var ss) && ss.GetBoolean();

        path = Path.GetFullPath(path);

        if (!Directory.Exists(path))
            return Task.FromResult(ToolResult.Error($"Directory not found: {path}"));

        try
        {
            var sb = new StringBuilder();
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            const int maxEntries = 1000;
            var count = 0;

            var entries = Directory.GetFileSystemEntries(path, "*", searchOption)
                .OrderBy(e => !Directory.Exists(e))
                .ThenBy(e => recursive ? Path.GetRelativePath(path, e) : Path.GetFileName(e));

            foreach (var entry in entries)
            {
                var name = recursive ? Path.GetRelativePath(path, entry) : Path.GetFileName(entry);
                var isDir = Directory.Exists(entry);

                // Skip hidden files unless requested
                var fileName = Path.GetFileName(entry);
                if (!showHidden && fileName.StartsWith('.'))
                    continue;

                if (isDir)
                {
                    sb.AppendLine($"{name}/");
                }
                else if (showSize)
                {
                    var info = new FileInfo(entry);
                    sb.AppendLine($"{name}  ({FormatSize(info.Length)})");
                }
                else
                {
                    sb.AppendLine(name);
                }

                count++;
                if (count >= maxEntries)
                {
                    sb.AppendLine($"... (truncated at {maxEntries} entries)");
                    break;
                }
            }

            var header = $"{count} entries";
            if (count == 0)
                return Task.FromResult(ToolResult.Success("(empty directory)"));

            return Task.FromResult(ToolResult.Success($"{header}\n{sb}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Error listing directory: {ex.Message}"));
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}
