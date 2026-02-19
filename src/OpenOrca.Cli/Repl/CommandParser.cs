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
    Export
}

public sealed record ParsedCommand(SlashCommand Command, string[] Args);

public sealed class CommandParser
{
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
            _ => null
        };
    }
}
