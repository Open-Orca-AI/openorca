using OpenOrca.Cli.Rendering;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class ConsoleHelperTests
{
    [Fact]
    public void VisibleLength_PlainText_ReturnsCorrectLength()
    {
        Assert.Equal(5, ConsoleHelper.VisibleLength("hello"));
    }

    [Fact]
    public void VisibleLength_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, ConsoleHelper.VisibleLength(""));
    }

    [Fact]
    public void VisibleLength_AnsiColorCodes_StripsEscapes()
    {
        // \x1b[36m = cyan, \x1b[0m = reset
        var text = "\x1b[36mhello\x1b[0m";
        Assert.Equal(5, ConsoleHelper.VisibleLength(text));
    }

    [Fact]
    public void VisibleLength_MultipleCodes_StripsAll()
    {
        var text = "\x1b[1m\x1b[31mBold Red\x1b[0m";
        Assert.Equal(8, ConsoleHelper.VisibleLength(text));
    }

    [Fact]
    public void VisibleLength_NoEscapes_ReturnsStringLength()
    {
        var text = "no escape codes here";
        Assert.Equal(text.Length, ConsoleHelper.VisibleLength(text));
    }

    [Fact]
    public void VisibleLength_OnlyEscapes_ReturnsZero()
    {
        var text = "\x1b[36m\x1b[0m";
        Assert.Equal(0, ConsoleHelper.VisibleLength(text));
    }

    [Fact]
    public void GetConsoleWidth_ReturnsPositiveValue()
    {
        // In test runners, Console.WindowWidth may throw — but the method should handle it
        var width = ConsoleHelper.GetConsoleWidth();
        Assert.True(width > 0);
    }
}
