using System.Diagnostics;
using System.Text;

namespace OpenOrca.Cli.Rendering;

/// <summary>
/// Buffers streaming response tokens and periodically re-renders the accumulated
/// text through <see cref="MarkdownRenderer"/> for rich terminal output.
/// Hides the cursor during re-renders to eliminate flicker.
/// </summary>
internal sealed class MarkdownStreamRenderer
{
    private readonly StringBuilder _buffer = new();
    private readonly Stopwatch _renderTimer = new();
    private readonly MarkdownRenderer _renderer = new();
    private int _startLine = -1;
    private bool _active;

    /// <summary>Minimum interval between re-renders to avoid flicker.</summary>
    private const long RenderIntervalMs = 80;

    /// <summary>Whether markdown streaming is actively rendering.</summary>
    public bool Active => _active;

    /// <summary>
    /// Begin markdown streaming. Captures the current cursor position as the
    /// render origin. Call when the first visible response token arrives.
    /// </summary>
    public void Start()
    {
        _startLine = Console.CursorTop;
        _active = true;
        _renderTimer.Restart();
    }

    /// <summary>
    /// Append a filtered token to the buffer. Re-renders if the throttle interval has elapsed.
    /// </summary>
    public void AppendToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        _buffer.Append(token);

        if (!_active) return;

        if (_renderTimer.ElapsedMilliseconds >= RenderIntervalMs)
        {
            Render();
            _renderTimer.Restart();
        }
    }

    /// <summary>
    /// Perform a final render of the complete accumulated text and deactivate.
    /// </summary>
    public void Finish()
    {
        if (_active)
        {
            if (_buffer.Length > 0)
                Render();
            _active = false;
        }
    }

    /// <summary>
    /// Clear the rendered output area and deactivate without a final render.
    /// Used when switching display modes (e.g. Ctrl+O toggle to visible).
    /// </summary>
    public void Clear()
    {
        if (_active)
        {
            try
            {
                Console.SetCursorPosition(0, _startLine);
                Console.Write("\x1b[J");
            }
            catch { /* cursor manipulation failed */ }
            _active = false;
        }
    }

    private void Render()
    {
        var text = _buffer.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            Console.Write("\x1b[?25l"); // hide cursor
            Console.SetCursorPosition(0, _startLine);
            Console.Write("\x1b[J"); // clear from cursor to end of screen
            _renderer.RenderToConsole(text);
            Console.Write("\x1b[?25h"); // show cursor
        }
        catch
        {
            Console.Write("\x1b[?25h"); // ensure cursor visible on failure
            _active = false;
        }
    }
}
