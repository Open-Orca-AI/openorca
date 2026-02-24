namespace OpenOrca.Cli.Repl;

public enum SlashCommand
{
    None,
    Help,
    Clear,
    Exit,
    Model,
    Config,
    Session,
    Plan,
    Compact,
    Rewind,
    Context,
    Stats,
    Memory,
    Doctor,
    Copy,
    Export,
    Init,
    Diff,
    Undo,
    Rename,
    Add,
    Ask,
    Checkpoint,
    Fork,
    Review,
    Benchmark,
    Docs,
    CustomCommand
}

public sealed record ParsedCommand(SlashCommand Command, string[] Args);

public sealed class CommandParser
{
    private HashSet<string>? _customCommandNames;

    /// <summary>
    /// Set the known custom command names for fallback resolution.
    /// </summary>
    public void SetCustomCommandNames(IEnumerable<string> names)
    {
        _customCommandNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    public ParsedCommand? TryParse(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
            return null;

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1..] : [];

        return cmd switch
        {
            "/help" or "/h" or "/?" => new ParsedCommand(SlashCommand.Help, args),
            "/clear" or "/c" => new ParsedCommand(SlashCommand.Clear, args),
            "/exit" or "/quit" or "/q" => new ParsedCommand(SlashCommand.Exit, args),
            "/model" or "/m" => new ParsedCommand(SlashCommand.Model, args),
            "/config" => new ParsedCommand(SlashCommand.Config, args),
            "/session" or "/s" => new ParsedCommand(SlashCommand.Session, args),
            "/plan" or "/p" => new ParsedCommand(SlashCommand.Plan, args),
            "/compact" => new ParsedCommand(SlashCommand.Compact, args),
            "/rewind" => new ParsedCommand(SlashCommand.Rewind, args),
            "/context" or "/ctx" => new ParsedCommand(SlashCommand.Context, args),
            "/stats" or "/cost" => new ParsedCommand(SlashCommand.Stats, args),
            "/memory" => new ParsedCommand(SlashCommand.Memory, args),
            "/doctor" or "/diag" => new ParsedCommand(SlashCommand.Doctor, args),
            "/copy" or "/cp" => new ParsedCommand(SlashCommand.Copy, args),
            "/export" => new ParsedCommand(SlashCommand.Export, args),
            "/init" => new ParsedCommand(SlashCommand.Init, args),
            "/diff" => new ParsedCommand(SlashCommand.Diff, args),
            "/undo" => new ParsedCommand(SlashCommand.Undo, args),
            "/rename" => new ParsedCommand(SlashCommand.Rename, args),
            "/add" => new ParsedCommand(SlashCommand.Add, args),
            "/ask" => new ParsedCommand(SlashCommand.Ask, args),
            "/checkpoint" or "/cp!" => new ParsedCommand(SlashCommand.Checkpoint, args),
            "/fork" or "/f!" => new ParsedCommand(SlashCommand.Fork, args),
            "/review" => new ParsedCommand(SlashCommand.Review, args),
            "/benchmark" or "/bench" => new ParsedCommand(SlashCommand.Benchmark, args),
            "/docs" or "/doc" => new ParsedCommand(SlashCommand.Docs, args),
            _ => TryParseCustomCommand(cmd, args)
        };
    }

    private ParsedCommand? TryParseCustomCommand(string cmd, string[] args)
    {
        if (_customCommandNames is null)
            return null;

        // cmd is like "/review-pr" — strip the leading /
        var name = cmd[1..];
        if (_customCommandNames.Contains(name))
        {
            // Pack the command name as args[0], user args follow
            var customArgs = new string[args.Length + 1];
            customArgs[0] = name;
            Array.Copy(args, 0, customArgs, 1, args.Length);
            return new ParsedCommand(SlashCommand.CustomCommand, customArgs);
        }

        return null;
    }
}
