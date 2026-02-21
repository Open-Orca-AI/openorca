using System.Text.RegularExpressions;
using Spectre.Console;

namespace OpenOrca.Cli.Rendering;

public sealed class StreamingRenderer
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedListRegex = new(@"^(\s*)[-*+]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex OrderedListRegex = new(@"^(\s*)(\d+)\.\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex BlockquoteRegex = new(@"^>\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex HorizontalRuleRegex = new(@"^(---+|\*\*\*+|___+)$", RegexOptions.Compiled);
    private static readonly Regex CodeFenceRegex = new(@"^```", RegexOptions.Compiled);
    private static readonly Regex CodeSpanRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Compiled);

    private string _buffer = "";
    private string _currentLine = "";
    private bool _inCodeBlock;

    /// <summary>
    /// ANSI escape sequence to restore after re-rendering a markdown line (e.g., "\x1b[36m" for cyan).
    /// </summary>
    public string DefaultAnsiColor { get; set; } = "";

    public void Clear()
    {
        _buffer = "";
        _currentLine = "";
        _inCodeBlock = false;
    }

    public void AppendToken(string token)
    {
        _buffer += token;

        // Fast path: no newlines in token
        if (!token.Contains('\n'))
        {
            _currentLine += token;
            Console.Write(token);
            return;
        }

        // Process token with newlines — re-render completed lines
        var remaining = token;
        while (remaining.Length > 0)
        {
            var nlIndex = remaining.IndexOf('\n');
            if (nlIndex >= 0)
            {
                var beforeNl = remaining[..nlIndex];
                _currentLine += beforeNl;
                Console.Write(beforeNl);
                Console.Write('\n');
                TryReRenderLine(_currentLine);
                _currentLine = "";
                remaining = remaining[(nlIndex + 1)..];
            }
            else
            {
                _currentLine += remaining;
                Console.Write(remaining);
                remaining = "";
            }
        }
    }

    public void Finish()
    {
        if (_buffer.Length > 0 && !_buffer.EndsWith('\n'))
        {
            // Try to re-render the final partial line before finishing
            Console.Write('\n');
            TryReRenderLine(_currentLine);
        }
        _buffer = "";
        _currentLine = "";
        _inCodeBlock = false;
    }

    public string GetBuffer() => _buffer;

    private void TryReRenderLine(string line)
    {
        var trimmed = line.TrimEnd('\r');

        // Toggle code fence state
        if (CodeFenceRegex.IsMatch(trimmed.TrimStart()))
        {
            _inCodeBlock = !_inCodeBlock;
            return;
        }

        // Don't re-render inside code blocks
        if (_inCodeBlock) return;

        var formatted = FormatMarkdownLine(trimmed);
        if (formatted == null) return;

        // Re-render: cursor up, clear line, reset style, write formatted, newline, restore color
        Console.Write("\x1b[A\x1b[2K\r\x1b[0m");
        AnsiConsole.Markup(formatted);
        Console.Write('\n');
        if (DefaultAnsiColor.Length > 0)
            Console.Write(DefaultAnsiColor);
    }

    private static string? FormatMarkdownLine(string line)
    {
        // Heading: # text, ## text, etc.
        var match = HeadingRegex.Match(line);
        if (match.Success)
        {
            var level = match.Groups[1].Value.Length;
            var prefix = Markup.Escape(match.Groups[1].Value);
            var content = ApplyInlineFormatting(match.Groups[2].Value);
            var color = level switch
            {
                1 => "bold cyan",
                2 => "bold blue",
                3 => "bold yellow",
                4 => "bold green",
                5 => "bold magenta",
                _ => "bold grey"
            };
            return $"[{color}]{prefix} {content}[/]";
        }

        // Unordered list: - text, * text, + text
        match = UnorderedListRegex.Match(line);
        if (match.Success)
        {
            var indent = match.Groups[1].Value;
            var content = ApplyInlineFormatting(match.Groups[2].Value);
            return $"{indent}[cyan]•[/] {content}";
        }

        // Ordered list: 1. text
        match = OrderedListRegex.Match(line);
        if (match.Success)
        {
            var indent = match.Groups[1].Value;
            var num = match.Groups[2].Value;
            var content = ApplyInlineFormatting(match.Groups[3].Value);
            return $"{indent}[cyan]{num}.[/] {content}";
        }

        // Blockquote: > text
        match = BlockquoteRegex.Match(line);
        if (match.Success)
        {
            var content = ApplyInlineFormatting(match.Groups[1].Value);
            return $"[grey]│[/] {content}";
        }

        // Horizontal rule: ---, ***, ___
        if (HorizontalRuleRegex.IsMatch(line.Trim()))
        {
            var width = 60;
            try { width = Math.Min(Console.WindowWidth - 1, 60); }
            catch { /* use default if console width unavailable */ }
            return $"[grey]{new string('─', width)}[/]";
        }

        // Regular text — apply inline formatting if markdown syntax present
        if (line.Contains('`') || line.Contains('*'))
        {
            var formatted = ApplyInlineFormatting(line);
            if (formatted != Markup.Escape(line))
                return formatted;
        }

        return null;
    }

    private static string ApplyInlineFormatting(string text)
    {
        var result = Markup.Escape(text);

        // Extract code spans into placeholders (protect from bold/italic matching)
        var codeSpans = new List<string>();
        result = CodeSpanRegex.Replace(result, m =>
        {
            codeSpans.Add(m.Groups[1].Value);
            return $"\x00CS{codeSpans.Count - 1}\x00";
        });

        // Bold: **text**
        result = BoldRegex.Replace(result, "[bold]$1[/]");

        // Italic: *text* (not adjacent to another *)
        result = ItalicRegex.Replace(result, "[italic]$1[/]");

        // Restore code spans with formatting
        for (var i = 0; i < codeSpans.Count; i++)
            result = result.Replace($"\x00CS{i}\x00", $"[grey on grey23]{codeSpans[i]}[/]");

        return result;
    }
}
