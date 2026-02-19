using System.Text.RegularExpressions;

namespace OpenOrca.Cli.Rendering;

/// <summary>
/// Terminal-width utilities for ANSI-aware layout calculations.
/// </summary>
public static partial class ConsoleHelper
{
    /// <summary>
    /// Gets the current console width, falling back to 120 if unavailable.
    /// </summary>
    public static int GetConsoleWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            return 120;
        }
    }

    /// <summary>
    /// Returns the visible character count of a string, stripping ANSI escape sequences.
    /// </summary>
    public static int VisibleLength(string text)
    {
        return AnsiEscapeRegex().Replace(text, "").Length;
    }

    [GeneratedRegex(@"\x1b\[[0-9;]*m")]
    private static partial Regex AnsiEscapeRegex();
}
