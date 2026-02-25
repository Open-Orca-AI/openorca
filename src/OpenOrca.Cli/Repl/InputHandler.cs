using System.Text;
using RadLine;
using Spectre.Console;

namespace OpenOrca.Cli.Repl;

public sealed class InputHandler
{
    private readonly ReplState _state;
    private readonly TerminalPanel _panel;
    private readonly InjectableInputSource _inputSource;
    private readonly LineEditor? _editor;
    private bool _modeCycled;
    private string _savedInput = string.Empty;

    public InputHandler(ReplState state, TerminalPanel panel)
    {
        _state = state;
        _panel = panel;
        _inputSource = new InjectableInputSource();

        // Create the editor once so history persists across turns
        if (!Console.IsInputRedirected && LineEditor.IsSupported(AnsiConsole.Console))
        {
            _editor = new LineEditor(AnsiConsole.Console, _inputSource)
            {
                Prompt = new ModePrompt(state),
            };
            _editor.KeyBindings.Add(ConsoleKey.Tab, ConsoleModifiers.Shift,
                () => new CycleModeCommand(state, text =>
                {
                    _modeCycled = true;
                    _savedInput = text;
                }));

            _editor.KeyBindings.Add(ConsoleKey.O, ConsoleModifiers.Control,
                () => new CycleVerbosityCommand(state, panel));

            // Bind plain Up/Down to history (RadLine defaults to Ctrl+Up/Ctrl+Down)
            _editor.KeyBindings.Add<PreviousHistoryCommand>(ConsoleKey.UpArrow);
            _editor.KeyBindings.Add<NextHistoryCommand>(ConsoleKey.DownArrow);
        }
    }

    public async Task<string?> ReadInputAsync(CancellationToken ct)
    {
        // Non-interactive fallback (piped input / CI)
        if (_editor is null)
            return ReadNonInteractive();

        var prompt = (ModePrompt)_editor.Prompt;

        while (true)
        {
            _modeCycled = false;
            prompt.Continuation = false;

            if (_savedInput.Length > 0)
            {
                _inputSource.Inject(_savedInput);
                _savedInput = string.Empty;
            }

            _panel.EnterInput();

            string? result;
            try
            {
                result = await _editor.ReadLine(ct);
            }
            catch (OperationCanceledException)
            {
                _panel.ExitInput();
                return null;
            }

            _panel.ExitInput();

            if (result is null)
            {
                if (_modeCycled)
                {
                    _panel.Redraw();
                    continue;
                }
                return null; // Ctrl+C / EOF
            }

            var trimmed = result.Trim();

            // Backslash continuation
            if (!trimmed.EndsWith('\\'))
            {
                if (trimmed.Length > 0)
                    _editor.History.Add(trimmed);
                return trimmed;
            }

            var sb = new StringBuilder();
            sb.Append(trimmed[..^1]);
            sb.Append('\n');

            prompt.Continuation = true;
            while (true)
            {
                string? next;
                try
                {
                    next = await _editor.ReadLine(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (next is null)
                    break;

                if (next.TrimEnd().EndsWith('\\'))
                {
                    sb.Append(next.TrimEnd()[..^1]);
                    sb.Append('\n');
                }
                else
                {
                    sb.Append(next);
                    break;
                }
            }

            var final = sb.ToString().Trim();
            if (final.Length > 0)
                _editor.History.Add(final);
            return final;
        }
    }

    private static string? ReadNonInteractive()
    {
        var line = Console.ReadLine();
        if (line is null)
            return null;

        while (line.EndsWith('\\'))
        {
            line = line[..^1] + "\n";
            var next = Console.ReadLine();
            if (next is null)
                break;
            line += next;
        }

        return line.Trim();
    }
}

/// <summary>
/// Dynamic prompt that renders the current input mode indicator.
/// </summary>
internal sealed class ModePrompt : ILineEditorPrompt
{
    private readonly ReplState _state;

    /// <summary>
    /// When true, renders a continuation-line prompt instead of the mode prompt.
    /// </summary>
    public bool Continuation { get; set; }

    public ModePrompt(ReplState state) => _state = state;

    public (Markup Markup, int Margin) GetPrompt(ILineEditorState state, int line)
    {
        if (Continuation || line > 0)
            return (new Markup("[blue]  [/]"), 1);

        return _state.Mode switch
        {
            InputMode.Plan => (new Markup("[cyan][[plan]][/] [blue bold]❯[/]"), 1),
            InputMode.Ask => (new Markup("[magenta][[ask]][/] [blue bold]❯[/]"), 1),
            _ => (new Markup("[blue bold]❯[/]"), 1),
        };
    }
}

/// <summary>
/// Shift+Tab command: cycle input mode, preserve the current buffer text,
/// and cancel ReadLine so the editor re-enters with the updated prompt.
/// </summary>
internal sealed class CycleModeCommand : LineEditorCommand
{
    private readonly ReplState _state;
    private readonly Action<string> _onCycled;

    public CycleModeCommand(ReplState state, Action<string> onCycled)
    {
        _state = state;
        _onCycled = onCycled;
    }

    public override void Execute(LineEditorContext context)
    {
        _state.CycleMode();
        _onCycled(context.Buffer.Content);
        context.Submit(SubmitAction.Cancel);
    }
}

/// <summary>
/// Ctrl+O command: cycle verbosity level (0→1→2→3→4→0) and redraw the panel
/// to show the updated level. Does not cancel the current input.
/// </summary>
internal sealed class CycleVerbosityCommand : LineEditorCommand
{
    private readonly ReplState _state;
    private readonly TerminalPanel _panel;

    public CycleVerbosityCommand(ReplState state, TerminalPanel panel)
    {
        _state = state;
        _panel = panel;
    }

    public override void Execute(LineEditorContext context)
    {
        _state.Verbosity = (_state.Verbosity + 1) % 5;
        _panel.Redraw();
    }
}

/// <summary>
/// Input source that wraps console input but allows injecting keystrokes
/// (used to restore text after a mode cycle).
/// </summary>
internal sealed class InjectableInputSource : IInputSource
{
    private readonly Queue<ConsoleKeyInfo> _queue = new();

    public bool ByPassProcessing => false;

    public void Inject(string text)
    {
        foreach (var ch in text)
            _queue.Enqueue(new ConsoleKeyInfo(ch, 0, false, false, false));
    }

    public bool IsKeyAvailable() => _queue.Count > 0 || Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey()
    {
        if (_queue.TryDequeue(out var key))
            return key;
        return Console.ReadKey(intercept: true);
    }
}
