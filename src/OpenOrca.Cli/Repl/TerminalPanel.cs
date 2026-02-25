using System.Runtime.InteropServices;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Manages a fixed 4-line input panel at the bottom of the terminal using ANSI scroll regions.
/// Layout (from bottom):
///   Row H-4: ────────────── (top rule)
///   Row H-3: ❯ input here   (prompt line)
///   Row H-2: ────────────── (bottom rule)
///   Row H-1: ● Normal · …   (status line)
///
/// All output goes through the captured real stdout so that redraws work even when
/// Console.Out has been redirected (e.g. during streaming suppression).
/// </summary>
public sealed class TerminalPanel
{
    private readonly ReplState _state;
    private readonly TextWriter _out;
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
        // Capture the real stdout before anything can redirect Console.Out
        _out = Console.Out;
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
        _out.Write($"\x1b[1;{scrollBottom}r");

        IsActive = true;

        DrawPanel();

        // Move cursor to top-left of scroll region AFTER drawing panel
        _out.Write("\x1b[1;1H");
    }

    /// <summary>
    /// Redraw just the panel lines (e.g. after mode change or verbosity toggle).
    /// Saves/restores cursor position. Works even when Console.Out is redirected.
    /// </summary>
    public void Redraw()
    {
        if (!IsActive)
            return;

        // Save cursor position
        _out.Write("\x1b[s");
        DrawPanel();
        // Restore cursor position
        _out.Write("\x1b[u");
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
        _out.Write("\x1b[2K");
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
        _out.Write("\x1b[r");
        IsActive = false;
    }

    /// <summary>
    /// Draw the 4-line panel at the bottom of the terminal.
    /// Uses raw ANSI codes written to the real stdout to avoid
    /// Spectre.Console cursor positioning conflicts and Console.Out redirection.
    /// </summary>
    private void DrawPanel()
    {
        var width = _lastWindowWidth;
        var h = _lastWindowHeight;
        var rule = new string('─', width);

        // Row H-4 (0-indexed: h-4): top rule
        _out.Write($"\x1b[{h - 3};1H\x1b[2K\x1b[90m{rule}\x1b[0m");

        // Row H-3 (0-indexed: h-3): prompt line — cleared but not written (RadLine draws here)
        _out.Write($"\x1b[{h - 2};1H\x1b[2K");

        // Row H-2 (0-indexed: h-2): bottom rule
        _out.Write($"\x1b[{h - 1};1H\x1b[2K\x1b[90m{rule}\x1b[0m");

        // Row H-1 (0-indexed: h-1): status line
        _out.Write($"\x1b[{h};1H\x1b[2K{BuildStatusLine()}");
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

        var verbosityLabel = _state.Verbosity switch
        {
            0 => "quiet",
            1 => "normal",
            2 => "full output",
            3 => "thinking",
            4 => "full thinking",
            _ => $"level {_state.Verbosity}"
        };

        return $"  {dot} {modeName} \x1b[90m· Shift+Tab: mode · Ctrl+O: verbosity [\x1b[0m\x1b[33m{_state.Verbosity}\x1b[0m\x1b[90m/{verbosityLabel}] · /help\x1b[0m";
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
