using Spectre.Console;

namespace OpenOrca.Cli.Rendering;

public sealed class StreamingRenderer
{
    private readonly MarkdownRenderer _markdownRenderer = new();
    private string _buffer = "";
    private bool _useMarkdown = true;

    public bool UseMarkdown
    {
        get => _useMarkdown;
        set => _useMarkdown = value;
    }

    public void Clear()
    {
        _buffer = "";
    }

    public void AppendToken(string token)
    {
        _buffer += token;
        // Write raw text directly — never parse as Spectre markup
        Console.Write(token);
    }

    public void Finish()
    {
        if (_buffer.Length > 0 && !_buffer.EndsWith('\n'))
            AnsiConsole.WriteLine();
        _buffer = "";
    }

    /// <summary>
    /// Render the complete response with markdown formatting.
    /// Call this after the full response is collected for a polished display.
    /// </summary>
    public void RenderComplete(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        if (_useMarkdown && ContainsMarkdown(content))
        {
            _markdownRenderer.RenderToConsole(content);
        }
        else
        {
            AnsiConsole.MarkupLine(Markup.Escape(content));
        }
    }

    private static bool ContainsMarkdown(string text)
    {
        // Quick heuristic to detect if text contains markdown formatting
        return text.Contains("```") ||
               text.Contains("# ") ||
               text.Contains("**") ||
               text.Contains("- ") ||
               text.Contains("1. ") ||
               text.Contains("`") ||
               text.Contains("[](" ) ||
               text.Contains("| ");
    }

    public string GetBuffer() => _buffer;
}
