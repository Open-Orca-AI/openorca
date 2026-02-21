using System.Text;
using Spectre.Console;

namespace OpenOrca.Cli.Rendering;

/// <summary>
/// Renders unified diff text with colorized lines in Spectre.Console panels.
/// </summary>
public static class DiffRenderer
{
    /// <summary>
    /// Render a unified diff with colorized lines inside a bordered panel.
    /// </summary>
    public static void RenderUnifiedDiff(string diffText, string header, Color borderColor)
    {
        if (string.IsNullOrWhiteSpace(diffText))
        {
            AnsiConsole.MarkupLine("[grey]No changes.[/]");
            return;
        }

        // Truncate oversized diffs
        if (diffText.Length > CliConstants.BashOutputMaxChars)
            diffText = diffText[..CliConstants.BashOutputMaxChars] + "\n... (truncated)";

        var markupText = ColorizeDiffText(diffText);
        AnsiConsole.Write(new Panel(new Markup(markupText))
            .Header($"[bold]{Markup.Escape(header)}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(borderColor));
    }

    /// <summary>
    /// Parse unified diff lines and return the raw Spectre markup string.
    /// </summary>
    internal static string ColorizeDiffText(string diffText)
    {
        var sb = new StringBuilder();
        var lines = diffText.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (i > 0)
                sb.Append('\n');

            var escaped = Markup.Escape(line);

            if (line.StartsWith("---") || line.StartsWith("+++"))
                sb.Append($"[bold]{escaped}[/]");
            else if (line.StartsWith("@@"))
                sb.Append($"[cyan]{escaped}[/]");
            else if (line.StartsWith("diff "))
                sb.Append($"[bold yellow]{escaped}[/]");
            else if (line.StartsWith('+'))
                sb.Append($"[green]{escaped}[/]");
            else if (line.StartsWith('-'))
                sb.Append($"[red]{escaped}[/]");
            else
                sb.Append($"[dim]{escaped}[/]");
        }

        return sb.ToString();
    }
}
