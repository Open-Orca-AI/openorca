namespace OpenOrca.Tools.Utilities;

/// <summary>
/// Fixes double-escaped sequences from local models that emit \\n instead of \n in JSON.
/// After JSON parsing, the string contains literal backslash+n instead of real newlines.
/// </summary>
internal static class StringEscapeHelper
{
    public static string UnescapeLiteralSequences(string value)
    {
        if (!value.Contains('\\')) return value;
        // Handle \\ first via placeholder so \\n doesn't get partially matched as \n
        return value
            .Replace("\\\\", "\x00")
            .Replace("\\n", "\n")
            .Replace("\\t", "\t")
            .Replace("\x00", "\\");
    }
}
