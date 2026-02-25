using System.Text.Json;
using System.Text.RegularExpressions;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

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
        var append = args.TryGetProperty("append", out var a) && a.GetBooleanLenient();

        path = Path.GetFullPath(path);

        // Reject empty content or very short content that's just a role tag (model hallucination)
        if (string.IsNullOrWhiteSpace(content))
        {
            return ToolResult.Error(
                $"Error: The content for '{Path.GetFileName(path)}' is empty. " +
                "Please include the FULL file content in the 'content' argument and try again.");
        }
        if (content.Trim().Length < 50 &&
            Regex.IsMatch(content.Trim(), @"^</?(assistant|user|system|tool)>$", RegexOptions.IgnoreCase))
        {
            return ToolResult.Error(
                $"Error: The content for '{Path.GetFileName(path)}' appears to be a role tag rather than file content. " +
                "Please include the FULL file content in the 'content' argument and try again.");
        }

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (append)
            {
                await FileRetryHelper.RetryOnIOExceptionAsync(() => File.AppendAllTextAsync(path, content, ct), ct);
                return ToolResult.Success($"Appended: {path} ({content.Length} chars added)");
            }

            await FileRetryHelper.RetryOnIOExceptionAsync(() => File.WriteAllTextAsync(path, content, ct), ct);
            return ToolResult.Success($"Written: {path} ({content.Length} chars)");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error writing file: {ex.Message}");
        }
    }
}
