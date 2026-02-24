using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.FileSystem;

public sealed class MultiEditTool : IOrcaTool
{
    public string Name => "multi_edit";
    public string Description => "Apply multiple edits across one or more files atomically. If any edit fails, all changes are rolled back. Each edit specifies a file path, old_string to find, and new_string to replace it with.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "edits": {
                "type": "array",
                "description": "Array of edits to apply. All edits are validated first, then applied atomically.",
                "items": {
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
                        }
                    },
                    "required": ["path", "old_string", "new_string"]
                }
            }
        },
        "required": ["edits"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("edits", out var editsArr) || editsArr.ValueKind != JsonValueKind.Array)
            return ToolResult.Error("'edits' must be a non-empty array.");

        var edits = new List<EditEntry>();
        foreach (var item in editsArr.EnumerateArray())
        {
            var path = item.TryGetProperty("path", out var p) ? p.GetString() : null;
            var oldString = item.TryGetProperty("old_string", out var o) ? o.GetString() : null;
            var newString = item.TryGetProperty("new_string", out var n) ? n.GetString() : null;
            var replaceAll = item.TryGetProperty("replace_all", out var ra) && ra.GetBoolean();

            if (string.IsNullOrEmpty(path) || oldString is null || newString is null)
                return ToolResult.Error("Each edit must have 'path', 'old_string', and 'new_string'.");

            edits.Add(new EditEntry(Path.GetFullPath(path), oldString, newString, replaceAll));
        }

        if (edits.Count == 0)
            return ToolResult.Error("'edits' array must not be empty.");

        // Phase 1: Validate — read all files and verify each edit is valid
        var snapshots = new Dictionary<string, string>(); // path → original content
        var validatedEdits = new List<(EditEntry Edit, string Content)>();

        foreach (var edit in edits)
        {
            if (!File.Exists(edit.Path))
                return ToolResult.Error($"File not found: {edit.Path}");

            if (!snapshots.ContainsKey(edit.Path))
                snapshots[edit.Path] = await File.ReadAllTextAsync(edit.Path, ct);

            // Work on the latest content (may have been modified by a previous edit in the batch)
            var content = validatedEdits
                .Where(v => v.Edit.Path == edit.Path)
                .Select(v => ApplyEdit(v.Content, v.Edit))
                .LastOrDefault() ?? snapshots[edit.Path];

            if (!content.Contains(edit.OldString))
                return ToolResult.Error($"old_string not found in {edit.Path}: \"{Truncate(edit.OldString, 80)}\"");

            if (!edit.ReplaceAll)
            {
                var count = CountOccurrences(content, edit.OldString);
                if (count > 1)
                    return ToolResult.Error(
                        $"old_string appears {count} times in {edit.Path}. Provide more context or set replace_all to true.");
            }

            validatedEdits.Add((edit, content));
        }

        // Phase 2: Apply — compute final content per file
        var finalContents = new Dictionary<string, string>(snapshots);
        foreach (var (edit, _) in validatedEdits)
        {
            finalContents[edit.Path] = ApplyEdit(finalContents[edit.Path], edit);
        }

        // Phase 3: Write — write all files, rollback on failure
        var writtenFiles = new List<string>();
        try
        {
            foreach (var (path, content) in finalContents)
            {
                await FileRetryHelper.RetryOnIOExceptionAsync(() => File.WriteAllTextAsync(path, content, ct), ct);
                writtenFiles.Add(path);
            }
        }
        catch (Exception ex)
        {
            // Rollback all written files
            foreach (var path in writtenFiles)
            {
                if (snapshots.TryGetValue(path, out var original))
                {
                    try { await File.WriteAllTextAsync(path, original, ct); }
                    catch { /* best effort rollback */ }
                }
            }
            return ToolResult.Error($"Failed to write {writtenFiles.Count} file(s), rolled back all changes: {ex.Message}");
        }

        // Build summary
        var sb = new StringBuilder();
        sb.AppendLine($"Applied {edits.Count} edit(s) across {finalContents.Count} file(s):");
        foreach (var edit in edits)
        {
            var count = edit.ReplaceAll ? CountOccurrences(snapshots.GetValueOrDefault(edit.Path, ""), edit.OldString) : 1;
            sb.AppendLine($"  - {edit.Path}: {count} replacement(s)");
        }
        return ToolResult.Success(sb.ToString().TrimEnd());
    }

    private static string ApplyEdit(string content, EditEntry edit)
    {
        return edit.ReplaceAll
            ? content.Replace(edit.OldString, edit.NewString)
            : ReplaceFirst(content, edit.OldString, edit.NewString);
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

    private static string Truncate(string text, int maxLen)
        => text.Length <= maxLen ? text : text[..maxLen] + "...";

    private sealed record EditEntry(string Path, string OldString, string NewString, bool ReplaceAll);
}
