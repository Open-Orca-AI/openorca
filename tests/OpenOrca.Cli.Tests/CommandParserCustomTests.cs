using OpenOrca.Cli.Repl;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class CommandParserCustomTests
{
    [Fact]
    public void CustomCommand_ResolvedFromKnownNames()
    {
        var parser = new CommandParser();
        parser.SetCustomCommandNames(["review-pr", "deploy"]);

        var result = parser.TryParse("/review-pr 123");

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.CustomCommand, result!.Command);
        Assert.Equal("review-pr", result.Args[0]);
        Assert.Equal("123", result.Args[1]);
    }

    [Fact]
    public void CustomCommand_NoArgs()
    {
        var parser = new CommandParser();
        parser.SetCustomCommandNames(["lint"]);

        var result = parser.TryParse("/lint");

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.CustomCommand, result!.Command);
        Assert.Equal("lint", result.Args[0]);
        Assert.Single(result.Args);
    }

    [Fact]
    public void UnknownCommand_ReturnsNull_WhenNotCustom()
    {
        var parser = new CommandParser();
        parser.SetCustomCommandNames(["deploy"]);

        var result = parser.TryParse("/unknown-cmd");

        Assert.Null(result);
    }

    [Fact]
    public void BuiltinCommand_TakesPriority()
    {
        var parser = new CommandParser();
        parser.SetCustomCommandNames(["help"]); // custom "help" should NOT override built-in

        var result = parser.TryParse("/help");

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.Help, result!.Command);
    }

    [Fact]
    public void Checkpoint_Parses()
    {
        var parser = new CommandParser();

        var result = parser.TryParse("/checkpoint list");

        Assert.NotNull(result);
        Assert.Equal(SlashCommand.Checkpoint, result!.Command);
        Assert.Equal("list", result.Args[0]);
    }

    [Fact]
    public void NoCustomNames_ReturnsNull()
    {
        var parser = new CommandParser();
        // Don't set custom names

        var result = parser.TryParse("/custom-thing");
        Assert.Null(result);
    }
}
