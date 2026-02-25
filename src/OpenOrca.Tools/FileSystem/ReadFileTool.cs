using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.FileSystem;

public sealed class ReadFileTool : IOrcaTool
{
    public string Name => "read_file";
    public string Description => "Read the contents of a file. Returns content with line numbers. Use offset/limit to read specific sections of large files. Detects binary files and returns an error instead of garbled output.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "The absolute or relative path to the file to read"
            },
            "offset": {
                "type": "integer",
                "description": "Line number to start reading from (1-based). Defaults to 1."
            },
            "limit": {
                "type": "integer",
                "description": "Maximum number of lines to read. Defaults to 2000."
            }
        },
        "required": ["path"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.GetProperty("path").GetString()!;
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32Lenient(1) : 1;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32Lenient(2000) : 2000;

        path = Path.GetFullPath(path);

        if (!File.Exists(path))
            return ToolResult.Error($"File not found: {path}");

        try
        {
            // Binary detection: check first 8KB for null bytes
            var probe = new byte[8192];
            await using (var stream = File.OpenRead(path))
            {
                var bytesRead = await stream.ReadAsync(probe.AsMemory(0, probe.Length), ct);
                for (var b = 0; b < bytesRead; b++)
                {
                    if (probe[b] == 0)
                        return ToolResult.Error($"Binary file detected: {path}. Use bash to inspect binary files (e.g., 'xxd' or 'file').");
                }
            }

            var lines = await File.ReadAllLinesAsync(path, ct);
            var totalLines = lines.Length;
            var startIndex = Math.Max(0, offset - 1);
            var endIndex = Math.Min(lines.Length, startIndex + limit);

            var sb = new StringBuilder();
            sb.AppendLine($"File: {path} ({totalLines} lines, showing {startIndex + 1}-{endIndex})");
            sb.AppendLine();

            var lineNumWidth = endIndex.ToString().Length;

            for (var i = startIndex; i < endIndex; i++)
            {
                var lineNum = (i + 1).ToString().PadLeft(lineNumWidth);
                var line = lines[i].Length > 2000 ? lines[i][..2000] + "..." : lines[i];
                sb.AppendLine($"{lineNum}\t{line}");
            }

            if (sb.Length == 0)
                return ToolResult.Success("(empty file)");

            return ToolResult.Success(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error reading file: {ex.Message}");
        }
    }
}
