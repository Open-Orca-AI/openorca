using Spectre.Console;

namespace OpenOrca.Cli.Repl;

public sealed class InputHandler
{
    private readonly ReplState _state;

    public InputHandler(ReplState state)
    {
        _state = state;
    }

    public string? ReadInput()
    {
        RenderPrompt();

        // Non-interactive fallback (piped input / CI)
        if (Console.IsInputRedirected)
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

        // Interactive key-by-key loop
        var buffer = new System.Text.StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            // Shift+Tab → cycle mode
            if (key.Key == ConsoleKey.Tab && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
            {
                _state.CycleMode();
                ClearCurrentLine(buffer.Length);
                buffer.Clear();
                RenderPrompt();
                continue;
            }

            // Tab → insert spaces (don't let raw tab through)
            if (key.Key == ConsoleKey.Tab)
            {
                buffer.Append("    ");
                Console.Write("    ");
                continue;
            }

            // Enter → submit
            if (key.Key == ConsoleKey.Enter)
            {
                // Check for multi-line continuation (trailing backslash)
                if (buffer.Length > 0 && buffer[^1] == '\\')
                {
                    buffer.Remove(buffer.Length - 1, 1);
                    buffer.Append('\n');
                    Console.WriteLine();
                    AnsiConsole.Markup("[blue]  [/] ");
                    continue;
                }

                Console.WriteLine();
                return buffer.ToString().Trim();
            }

            // Backspace → remove last char
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Remove(buffer.Length - 1, 1);
                    // Move cursor back, overwrite with space, move back again
                    Console.Write("\b \b");
                }
                continue;
            }

            // Escape → clear buffer
            if (key.Key == ConsoleKey.Escape)
            {
                ClearCurrentLine(buffer.Length);
                buffer.Clear();
                RenderPrompt();
                continue;
            }

            // Ignore other control keys (arrows, F-keys, etc.)
            if (key.KeyChar == '\0')
                continue;

            // Regular character → append and echo
            buffer.Append(key.KeyChar);
            Console.Write(key.KeyChar);
        }
    }

    private void RenderPrompt()
    {
        switch (_state.Mode)
        {
            case InputMode.Plan:
                AnsiConsole.Markup("[cyan][[plan]][/] [blue bold]❯[/] ");
                break;
            case InputMode.Ask:
                AnsiConsole.Markup("[magenta][[ask]][/] [blue bold]❯[/] ");
                break;
            default:
                AnsiConsole.Markup("[blue bold]❯[/] ");
                break;
        }
    }

    private static void ClearCurrentLine(int bufferLength)
    {
        // Move to start of line and clear it
        var currentLeft = Console.CursorLeft;
        if (currentLeft > 0)
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', currentLeft));
            Console.SetCursorPosition(0, Console.CursorTop);
        }
    }
}
