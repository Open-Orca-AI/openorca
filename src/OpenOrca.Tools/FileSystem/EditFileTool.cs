using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.FileSystem;

public sealed class EditFileTool : IOrcaTool
{
    public string Name => "edit_file";
    public string Description => "Perform an exact string replacement in a file. The old_string must be unique (or use replace_all). Set create_if_missing=true with an empty old_string to create a new file with new_string as content.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "The path to the file to edit"
            },
            "old_string": {
                "type": "string",
                "description": "The exact text to find and replace"
            },
            "new_string": {
                "type": "string",
                "description": "The text to replace it with"
            },
            "replace_all": {
                "type": "boolean",
                "description": "Replace all occurrences instead of requiring uniqueness. Defaults to false."
            },
            "create_if_missing": {
                "type": "boolean",
                "description": "When true and the file doesn't exist, create it with new_string as content (old_string must be empty). Defaults to false."
            }
        },
        "required": ["path", "old_string", "new_string"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.GetProperty("path").GetString()!;
        var oldString = args.GetProperty("old_string").GetString()!;
        var newString = args.GetProperty("new_string").GetString()!;
        var replaceAll = args.TryGetProperty("replace_all", out var ra) && ra.GetBoolean();
        var createIfMissing = args.TryGetProperty("create_if_missing", out var cim) && cim.GetBoolean();

        path = Path.GetFullPath(path);

        if (!File.Exists(path))
        {
            if (createIfMissing && string.IsNullOrEmpty(oldString))
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                await File.WriteAllTextAsync(path, newString, ct);
                return ToolResult.Success($"Created new file: {path} ({newString.Length} chars)");
            }
            return ToolResult.Error($"File not found: {path}. Use read_file to verify the path, or set create_if_missing=true to create it.");
        }

        try
        {
            var content = await File.ReadAllTextAsync(path, ct);

            if (!content.Contains(oldString))
                return ToolResult.Error($"old_string not found in {path}. Use read_file to see current content.");

            if (!replaceAll)
            {
                var count = CountOccurrences(content, oldString);
                if (count > 1)
                    return ToolResult.Error(
                        $"old_string appears {count} times in {path}. Provide more context to make it unique, or set replace_all to true.");
            }

            var newContent = replaceAll
                ? content.Replace(oldString, newString)
                : ReplaceFirst(content, oldString, newString);

            await File.WriteAllTextAsync(path, newContent, ct);

            var changeCount = replaceAll
                ? CountOccurrences(content, oldString)
                : 1;

            // Show snippet around the change (5 lines before/after)
            var snippet = GetChangeSnippet(newContent, newString);

            return ToolResult.Success($"Replaced {changeCount} occurrence(s) in {path}\n\n{snippet}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error editing file: {ex.Message}");
        }
    }

    private static string GetChangeSnippet(string content, string newString)
    {
        var pos = content.IndexOf(newString, StringComparison.Ordinal);
        if (pos < 0) return "";

        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        // Count newlines before pos to find the target line
        var targetLine = content[..pos].Split('\n').Length - 1;

        var start = Math.Max(0, targetLine - 5);
        var end = Math.Min(lines.Length - 1, targetLine + 5);

        var sb = new StringBuilder();
        sb.AppendLine("--- snippet ---");
        for (var i = start; i <= end; i++)
        {
            sb.AppendLine($"  {i + 1}\t{lines[i]}");
        }
        return sb.ToString();
    }

    private static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        var pos = text.IndexOf(oldValue, StringComparison.Ordinal);
        return pos < 0 ? text : string.Concat(text.AsSpan(0, pos), newValue, text.AsSpan(pos + oldValue.Length));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
