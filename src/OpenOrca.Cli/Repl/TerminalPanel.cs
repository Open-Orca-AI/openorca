namespace OpenOrca.Cli.Repl;

/// <summary>
/// Manages a fixed 4-line input panel at the bottom of the terminal using ANSI scroll regions.
/// Layout (from bottom):
///   Row H-4: ────────────── (top rule)
///   Row H-3: ❯ input here   (prompt line)
///   Row H-2: ────────────── (bottom rule)
///   Row H-1: ● Normal · …   (status line)
/// </summary>
public sealed class TerminalPanel
{
    private readonly ReplState _state;
    private int _lastWindowHeight;
    private int _lastWindowWidth;
    private int _savedCursorRow;

    /// <summary>Whether the panel is currently active (scroll region set).</summary>
    public bool IsActive { get; private set; }

    /// <summary>The row where output should resume after input.</summary>
    public int ScrollRegionBottom => IsActive ? Math.Max(_lastWindowHeight - 5, 4) : int.MaxValue;

    public TerminalPanel(ReplState state)
    {
        _state = state;
    }

    /// <summary>
    /// Set up the ANSI scroll region and draw the bottom panel.
    /// </summary>
    public void Setup()
    {
        if (!IsTerminalSupported())
            return;

        _lastWindowHeight = Console.WindowHeight;
        _lastWindowWidth = Console.WindowWidth;

        var scrollBottom = _lastWindowHeight - 4; // rows 1..H-4 (1-indexed for ANSI)

        // Set scroll region to upper portion
        Console.Write($"\x1b[1;{scrollBottom}r");

        IsActive = true;

        DrawPanel();

        // Move cursor to top-left of scroll region AFTER drawing panel
        Console.Write("\x1b[1;1H");
    }

    /// <summary>
    /// Redraw just the panel lines (e.g. after mode change). Saves/restores cursor position.
    /// </summary>
    public void Redraw()
    {
        if (!IsActive)
            return;

        // Save cursor position
        Console.Write("\x1b[s");
        DrawPanel();
        // Restore cursor position
        Console.Write("\x1b[u");
    }

    /// <summary>
    /// Prepare for input: save the current output cursor row, then move cursor to the prompt line.
    /// </summary>
    public void EnterInput()
    {
        if (!IsActive)
            return;

        // Check for terminal resize
        if (Console.WindowHeight != _lastWindowHeight || Console.WindowWidth != _lastWindowWidth)
        {
            // Re-setup with new dimensions
            Setup();
        }

        _savedCursorRow = Console.CursorTop;

        // Move cursor to prompt line (H-3, 0-indexed = H-4)
        var promptRow = _lastWindowHeight - 3;
        Console.SetCursorPosition(0, promptRow);

        // Clear the prompt line so RadLine starts fresh
        Console.Write("\x1b[2K");
    }

    /// <summary>
    /// After input: restore cursor to the saved output row in the scroll region.
    /// </summary>
    public void ExitInput()
    {
        if (!IsActive)
            return;

        Console.SetCursorPosition(0, _savedCursorRow);
    }

    /// <summary>
    /// Reset scroll region to full screen and clear panel state.
    /// </summary>
    public void Teardown()
    {
        if (!IsActive)
            return;

        // Reset scroll region to full terminal
        Console.Write("\x1b[r");
        IsActive = false;
    }

    /// <summary>
    /// Draw the 4-line panel at the bottom of the terminal.
    /// Uses raw ANSI codes to avoid Spectre.Console cursor positioning conflicts.
    /// </summary>
    private void DrawPanel()
    {
        var width = _lastWindowWidth;
        var h = _lastWindowHeight;
        var rule = new string('─', width);

        // Row H-4 (0-indexed: h-4): top rule
        Console.SetCursorPosition(0, h - 4);
        Console.Write("\x1b[2K");
        Console.Write($"\x1b[90m{rule}\x1b[0m");

        // Row H-3 (0-indexed: h-3): prompt line — cleared but not written (RadLine draws here)
        Console.SetCursorPosition(0, h - 3);
        Console.Write("\x1b[2K");

        // Row H-2 (0-indexed: h-2): bottom rule
        Console.SetCursorPosition(0, h - 2);
        Console.Write("\x1b[2K");
        Console.Write($"\x1b[90m{rule}\x1b[0m");

        // Row H-1 (0-indexed: h-1): status line
        Console.SetCursorPosition(0, h - 1);
        Console.Write("\x1b[2K");
        Console.Write(BuildStatusLine());
    }

    /// <summary>
    /// Build the ANSI-colored status line based on current mode.
    /// </summary>
    private string BuildStatusLine()
    {
        var (dot, modeName) = _state.Mode switch
        {
            InputMode.Plan => ("\x1b[36m●\x1b[0m", "\x1b[36mPlan\x1b[0m"),   // cyan
            InputMode.Ask => ("\x1b[35m●\x1b[0m", "\x1b[35mAsk\x1b[0m"),      // magenta
            _ => ("\x1b[34m●\x1b[0m", "\x1b[34mNormal\x1b[0m"),               // blue
        };

        return $"  {dot} {modeName} \x1b[90m· Shift+Tab: switch mode · /help: commands\x1b[0m";
    }

    private static bool IsTerminalSupported()
    {
        if (Console.IsInputRedirected)
            return false;

        try
        {
            return Console.WindowHeight >= 10;
        }
        catch
        {
            return false;
        }
    }
}
