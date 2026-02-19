using System.Text.Json;
using System.Text.RegularExpressions;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.FileSystem;

public sealed class WriteFileTool : IOrcaTool
{
    public string Name => "write_file";
    public string Description => "Write content to a file, creating it and parent directories if needed. By default overwrites existing content. Set append=true to append instead.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "The path to the file to write"
            },
            "content": {
                "type": "string",
                "description": "The content to write to the file"
            },
            "append": {
                "type": "boolean",
                "description": "Append to the file instead of overwriting. Defaults to false."
            }
        },
        "required": ["path", "content"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.GetProperty("path").GetString()!;
        var content = args.GetProperty("content").GetString()!;
        var append = args.TryGetProperty("append", out var a) && a.GetBoolean();

        path = Path.GetFullPath(path);

        // Reject empty or garbage content (e.g., bare role tags like "<assistant>")
        if (string.IsNullOrWhiteSpace(content) ||
            Regex.IsMatch(content.Trim(), @"^</?(assistant|user|system|tool)>$", RegexOptions.IgnoreCase))
        {
            return ToolResult.Error(
                $"Error: The content for '{Path.GetFileName(path)}' is empty or contains only a role tag instead of actual file content. " +
                "Please include the FULL file content in the 'content' argument and try again.");
        }

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (append)
            {
                await File.AppendAllTextAsync(path, content, ct);
                return ToolResult.Success($"Appended: {path} ({content.Length} chars added)");
            }

            await File.WriteAllTextAsync(path, content, ct);
            return ToolResult.Success($"Written: {path} ({content.Length} chars)");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error writing file: {ex.Message}");
        }
    }
}
