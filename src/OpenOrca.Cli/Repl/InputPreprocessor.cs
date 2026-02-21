using System.Text.RegularExpressions;
using Spectre.Console;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Preprocesses user input before it's added to the conversation.
/// Expands @file references to inline file contents.
/// </summary>
public static partial class InputPreprocessor
{
    private const int MaxFileChars = 50_000;

    // Matches @path tokens preceded by whitespace or start-of-line.
    [GeneratedRegex(@"(?<=^|\s)@([\w./\\-]+[\w.])", RegexOptions.Compiled)]
    private static partial Regex FileReferencePattern();

    /// <summary>
    /// Expand @file references in the input text. Files that exist are inlined;
    /// non-existent paths are left as-is. Returns the (possibly expanded) input.
    /// </summary>
    public static string ExpandFileReferences(string input, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.Contains('@'))
            return input;

        var matches = FileReferencePattern().Matches(input);
        if (matches.Count == 0)
            return input;

        var cwd = baseDirectory ?? Directory.GetCurrentDirectory();
        var expandedFiles = new List<string>();
        var result = input;

        // Process matches in reverse order to preserve offsets
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var relativePath = match.Groups[1].Value;

            // Resolve relative to base directory
            var fullPath = Path.GetFullPath(relativePath, cwd);

            if (!File.Exists(fullPath))
                continue;

            try
            {
                var content = File.ReadAllText(fullPath);
                if (content.Length > MaxFileChars)
                    content = content[..MaxFileChars] + "\n... (truncated)";

                var displayPath = Path.GetRelativePath(cwd, fullPath);
                var replacement = $"[File: {displayPath}]\n{content}\n[/File]";

                // Replace the @path token (including the @ prefix)
                result = result[..match.Index] + replacement + result[(match.Index + match.Length)..];
                expandedFiles.Add(displayPath);
            }
            catch
            {
                // If we can't read the file, leave the @reference as-is
            }
        }

        if (expandedFiles.Count > 0)
        {
            expandedFiles.Reverse(); // Back to original order
            AnsiConsole.MarkupLine($"[green]Attached: {Markup.Escape(string.Join(", ", expandedFiles))}[/]");
        }

        return result;
    }
}
