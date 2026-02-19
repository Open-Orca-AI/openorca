using OpenOrca.Cli.Repl;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class CommandParserTests
{
    private readonly CommandParser _parser = new();

    // ── Basic command recognition ──

    [Theory]
    [InlineData("/help", SlashCommand.Help)]
    [InlineData("/h", SlashCommand.Help)]
    [InlineData("/?", SlashCommand.Help)]
    [InlineData("/clear", SlashCommand.Clear)]
    [InlineData("/c", SlashCommand.Clear)]
    [InlineData("/exit", SlashCommand.Exit)]
    [InlineData("/quit", SlashCommand.Exit)]
    [InlineData("/q", SlashCommand.Exit)]
    [InlineData("/model", SlashCommand.Model)]
    [InlineData("/m", SlashCommand.Model)]
    [InlineData("/config", SlashCommand.Config)]
    [InlineData("/session", SlashCommand.Session)]
    [InlineData("/s", SlashCommand.Session)]
    [InlineData("/plan", SlashCommand.Plan)]
    [InlineData("/p", SlashCommand.Plan)]
    [InlineData("/compact", SlashCommand.Compact)]
    [InlineData("/rewind", SlashCommand.Rewind)]
    [InlineData("/context", SlashCommand.Context)]
    [InlineData("/ctx", SlashCommand.Context)]
    [InlineData("/stats", SlashCommand.Stats)]
    [InlineData("/cost", SlashCommand.Stats)]
    [InlineData("/memory", SlashCommand.Memory)]
    [InlineData("/doctor", SlashCommand.Doctor)]
    [InlineData("/diag", SlashCommand.Doctor)]
    [InlineData("/copy", SlashCommand.Copy)]
    [InlineData("/cp", SlashCommand.Copy)]
    [InlineData("/export", SlashCommand.Export)]
    public void Parse_ValidCommand_ReturnsCorrectSlashCommand(string input, SlashCommand expected)
    {
        var result = _parser.TryParse(input);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Command);
    }

    // ── Case insensitivity ──

    [Theory]
    [InlineData("/HELP")]
    [InlineData("/Help")]
    [InlineData("/hElP")]
    public void Parse_CaseInsensitive_ReturnsCommand(string input)
    {
        var result = _parser.TryParse(input);

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.Help, result.Command);
    }

    // ── Arguments parsing ──

    [Fact]
    public void Parse_CommandWithArgs_ExtractsArguments()
    {
        var result = _parser.TryParse("/session save my session name");

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.Session, result.Command);
        Assert.Equal(["save", "my", "session", "name"], result.Args);
    }

    [Fact]
    public void Parse_CommandWithSingleArg_ExtractsArgument()
    {
        var result = _parser.TryParse("/model gpt-4");

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.Model, result.Command);
        Assert.Single(result.Args);
        Assert.Equal("gpt-4", result.Args[0]);
    }

    [Fact]
    public void Parse_CommandWithNoArgs_ReturnsEmptyArgs()
    {
        var result = _parser.TryParse("/help");

        Assert.NotNull(result);
        Assert.Empty(result.Args);
    }

    // ── Non-command input ──

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        Assert.Null(_parser.TryParse(""));
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsNull()
    {
        Assert.Null(_parser.TryParse("   "));
    }

    [Fact]
    public void Parse_NullInput_ReturnsNull()
    {
        Assert.Null(_parser.TryParse(null!));
    }

    [Fact]
    public void Parse_NoSlashPrefix_ReturnsNull()
    {
        Assert.Null(_parser.TryParse("hello world"));
    }

    [Fact]
    public void Parse_UnknownCommand_ReturnsNull()
    {
        Assert.Null(_parser.TryParse("/unknown"));
    }

    [Fact]
    public void Parse_BashShortcut_ReturnsNull()
    {
        // Bash shortcuts start with ! not / — should not be parsed as slash commands
        Assert.Null(_parser.TryParse("!ls -la"));
    }

    // ── Edge cases ──

    [Fact]
    public void Parse_MultipleSpaces_HandledGracefully()
    {
        var result = _parser.TryParse("/session   save    name");

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.Session, result.Command);
        Assert.Equal(["save", "name"], result.Args);
    }

    [Fact]
    public void Parse_RewindWithNumber_ExtractsArg()
    {
        var result = _parser.TryParse("/rewind 3");

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.Rewind, result.Command);
        Assert.Single(result.Args);
        Assert.Equal("3", result.Args[0]);
    }

    [Fact]
    public void Parse_ExportWithPath_ExtractsArg()
    {
        var result = _parser.TryParse("/export /tmp/conversation.md");

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.Export, result.Command);
        Assert.Equal("/tmp/conversation.md", result.Args[0]);
    }

    [Fact]
    public void Parse_CompactWithInstructions_ExtractsArgs()
    {
        var result = _parser.TryParse("/compact focus on code changes");

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.Compact, result.Command);
        Assert.Equal(["focus", "on", "code", "changes"], result.Args);
    }
}
