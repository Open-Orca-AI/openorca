using Spectre.Console;

namespace OpenOrca.Cli.Repl;

public sealed class InputHandler
{
    public string? ReadInput()
    {
        AnsiConsole.Markup("[blue bold]❯[/] ");
        var line = Console.ReadLine();
        if (line is null)
            return null;

        // Support multi-line input with trailing backslash
        while (line.EndsWith('\\'))
        {
            line = line[..^1] + "\n";
            AnsiConsole.Markup("[blue]  [/] ");
            var next = Console.ReadLine();
            if (next is null)
                break;
            line += next;
        }

        return line.Trim();
    }
}
