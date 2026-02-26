using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

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
        var replaceAll = args.TryGetProperty("replace_all", out var ra) && ra.GetBooleanLenient();
        var createIfMissing = args.TryGetProperty("create_if_missing", out var cim) && cim.GetBooleanLenient();

        path = Path.GetFullPath(path);

        if (!File.Exists(path))
        {
            if (createIfMissing && string.IsNullOrEmpty(oldString))
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                await FileRetryHelper.RetryOnIOExceptionAsync(() => File.WriteAllTextAsync(path, newString, ct), ct);
                return ToolResult.Success($"Created new file: {path} ({newString.Length} chars)");
            }
            return ToolResult.Error($"File not found: {path}. Use read_file to verify the path, or set create_if_missing=true to create it.");
        }

        try
        {
            var content = await File.ReadAllTextAsync(path, ct);

            if (!content.Contains(oldString))
            {
                // Fallback: local models often double-escape \n in JSON — try unescaping
                var unescapedOld = StringEscapeHelper.UnescapeLiteralSequences(oldString);
                if (unescapedOld != oldString && content.Contains(unescapedOld))
                {
                    oldString = unescapedOld;
                    newString = StringEscapeHelper.UnescapeLiteralSequences(newString);
                }
                else
                {
                    // Fallback: local models often strip leading whitespace — try normalized match
                    var wsMatch = TryWhitespaceNormalizedMatch(content, oldString);
                    if (wsMatch != null)
                    {
                        newString = AdjustIndentation(newString, wsMatch, oldString);
                        oldString = wsMatch;
                    }
                    else
                    {
                        return ToolResult.Error($"old_string not found in {path}. Use read_file to see current content.");
                    }
                }
            }

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

            await FileRetryHelper.RetryOnIOExceptionAsync(() => File.WriteAllTextAsync(path, newContent, ct), ct);

            var changeCount = replaceAll
                ? CountOccurrences(content, oldString)
                : 1;

            // Show diff around the first change
            var diff = GetEditDiff(content, oldString, newString);

            return ToolResult.Success($"Replaced {changeCount} occurrence(s) in {path}\n\n{diff}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error editing file: {ex.Message}");
        }
    }

    internal static string GetEditDiff(string originalContent, string oldString, string newString)
    {
        var pos = originalContent.IndexOf(oldString, StringComparison.Ordinal);
        if (pos < 0) return "";

        var allLines = originalContent.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var startLine = originalContent[..pos].Split('\n').Length - 1;

        var oldLines = oldString.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var newLines = newString.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

        const int contextCount = 3;
        var contextStart = Math.Max(0, startLine - contextCount);
        var endLine = startLine + oldLines.Length - 1;
        var shift = newLines.Length - oldLines.Length;
        var contextEnd = Math.Min(allLines.Length - 1, endLine + contextCount);

        var sb = new StringBuilder();
        sb.AppendLine("--- diff ---");

        // Context lines before (same line numbers in old and new)
        for (var i = contextStart; i < startLine; i++)
            sb.AppendLine($"  {i + 1,4}   {allLines[i]}");

        // Removed lines (old file line numbers)
        for (var i = 0; i < oldLines.Length; i++)
            sb.AppendLine($"  {startLine + i + 1,4} - {oldLines[i]}");

        // Added lines (new file line numbers)
        for (var i = 0; i < newLines.Length; i++)
            sb.AppendLine($"  {startLine + i + 1,4} + {newLines[i]}");

        // Context lines after (shifted line numbers from new file)
        for (var i = endLine + 1; i <= contextEnd; i++)
            sb.AppendLine($"  {i + 1 + shift,4}   {allLines[i]}");

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

    /// <summary>
    /// Tries to find old_string in content by comparing with leading whitespace stripped from each line.
    /// Returns the actual matched substring from content (with original indentation), or null.
    /// </summary>
    internal static string? TryWhitespaceNormalizedMatch(string content, string oldString)
    {
        var oldLines = oldString.Split('\n');
        var contentLines = content.Split('\n');

        // Strip trailing \r from each line for consistent comparison
        var oldTrimmed = oldLines.Select(l => l.TrimStart().TrimEnd('\r')).ToArray();

        // Skip if old_string is empty or single whitespace-only line (too ambiguous)
        if (oldTrimmed.Length == 0 || (oldTrimmed.Length == 1 && oldTrimmed[0].Length == 0))
            return null;

        string? firstMatch = null;
        var matchCount = 0;

        for (var i = 0; i <= contentLines.Length - oldTrimmed.Length; i++)
        {
            var match = true;
            for (var j = 0; j < oldTrimmed.Length; j++)
            {
                if (contentLines[i + j].TrimStart().TrimEnd('\r') != oldTrimmed[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                matchCount++;
                if (matchCount == 1)
                    firstMatch = string.Join('\n', contentLines.Skip(i).Take(oldTrimmed.Length));
                if (matchCount > 1)
                    return null; // Ambiguous — multiple matches, bail out
            }
        }

        // Only return if we found exactly one match AND it differs from the original
        return firstMatch != null && firstMatch != oldString ? firstMatch : null;
    }

    /// <summary>
    /// Adjusts indentation of new_string to match the indentation found in the actual file match.
    /// Detects the indentation delta between what the model sent (old_string) and what the file has,
    /// then prepends that delta to each line of new_string.
    /// </summary>
    internal static string AdjustIndentation(string newString, string matchedOldString, string originalOldString)
    {
        var matchedFirstLine = matchedOldString.Split('\n')[0];
        var oldFirstLine = originalOldString.Split('\n')[0];

        var matchedIndent = GetLeadingWhitespace(matchedFirstLine);
        var oldIndent = GetLeadingWhitespace(oldFirstLine);

        // If the model already had enough indentation, no adjustment needed
        if (oldIndent.Length >= matchedIndent.Length)
            return newString;

        // The extra indentation the file has that the model omitted
        var delta = matchedIndent[oldIndent.Length..];

        var lines = newString.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                lines[i] = delta + lines[i];
        }

        return string.Join('\n', lines);
    }

    private static string GetLeadingWhitespace(string line)
    {
        var trimmed = line.TrimStart();
        return line[..(line.Length - trimmed.Length)];
    }
}
