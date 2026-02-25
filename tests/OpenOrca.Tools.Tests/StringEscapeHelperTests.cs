using OpenOrca.Tools.Utilities;
using Xunit;

namespace OpenOrca.Tools.Tests;

public class StringEscapeHelperTests
{
    [Fact]
    public void UnescapeLiteralSequences_ConvertsNewlines()
    {
        var input = "line1\\nline2\\nline3";
        var result = StringEscapeHelper.UnescapeLiteralSequences(input);
        Assert.Equal("line1\nline2\nline3", result);
    }

    [Fact]
    public void UnescapeLiteralSequences_ConvertsTabs()
    {
        var input = "col1\\tcol2";
        var result = StringEscapeHelper.UnescapeLiteralSequences(input);
        Assert.Equal("col1\tcol2", result);
    }

    [Fact]
    public void UnescapeLiteralSequences_ConvertsBackslashes()
    {
        // Input has literal \\  (double backslash after JSON parse)
        var input = "path\\\\to\\\\file";
        var result = StringEscapeHelper.UnescapeLiteralSequences(input);
        Assert.Equal("path\\to\\file", result);
    }

    [Fact]
    public void UnescapeLiteralSequences_NoBackslash_ReturnsOriginal()
    {
        var input = "no special chars here";
        var result = StringEscapeHelper.UnescapeLiteralSequences(input);
        Assert.Same(input, result);
    }

    [Fact]
    public void UnescapeLiteralSequences_MixedSequences()
    {
        var input = "first\\nsecond\\tthird";
        var result = StringEscapeHelper.UnescapeLiteralSequences(input);
        Assert.Equal("first\nsecond\tthird", result);
    }
}
