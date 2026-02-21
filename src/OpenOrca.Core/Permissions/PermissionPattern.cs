using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenOrca.Core.Permissions;

/// <summary>
/// A permission pattern like "Bash(git *)" or "Write(src/**)".
/// Matches a tool name and an argument glob against the relevant argument of a tool call.
/// </summary>
public sealed record PermissionPattern(string ToolName, string ArgumentGlob)
{
    private static readonly Regex PatternRegex = new(@"^(\w+)\((.+)\)$", RegexOptions.Compiled);

    /// <summary>
    /// Parse a pattern string like "Bash(git *)" into a PermissionPattern.
    /// Returns null if the pattern is malformed.
    /// </summary>
    public static PermissionPattern? Parse(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;

        var match = PatternRegex.Match(pattern.Trim());
        if (!match.Success)
            return null;

        return new PermissionPattern(match.Groups[1].Value, match.Groups[2].Value);
    }

    /// <summary>
    /// Check if this pattern matches a given tool invocation.
    /// </summary>
    public bool Matches(string toolName, string? relevantArg)
    {
        if (!ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrEmpty(relevantArg))
            return false;

        return WildcardMatch(relevantArg, ArgumentGlob);
    }

    /// <summary>
    /// Extract the relevant argument from a tool's JSON args for pattern matching.
    /// For Bash: the "command" property. For file tools: the "path" property.
    /// </summary>
    public static string? ExtractRelevantArg(string toolName, string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            // Bash-like tools: extract "command"
            if (toolName.Equals("bash", StringComparison.OrdinalIgnoreCase) ||
                toolName.Equals("start_background_process", StringComparison.OrdinalIgnoreCase))
            {
                return root.TryGetProperty("command", out var cmd) ? cmd.GetString() : null;
            }

            // File tools: extract "path"
            if (root.TryGetProperty("path", out var path))
                return path.GetString();

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Simple wildcard matching: * matches any characters (non-recursive),
    /// ** matches any characters including path separators (recursive).
    /// </summary>
    private static bool WildcardMatch(string input, string pattern)
    {
        // Convert glob pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", "##DOUBLESTAR##")
            .Replace("\\*", ".*")
            .Replace("##DOUBLESTAR##", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}
