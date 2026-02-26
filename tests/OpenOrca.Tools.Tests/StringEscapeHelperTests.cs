using OpenOrca.Tools.FileSystem;
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

public class WhitespaceNormalizedMatchTests
{
    [Fact]
    public void TryWhitespaceNormalizedMatch_FindsIndentedLine()
    {
        var content = "class Foo {\n    doStuff();\n}";
        var oldString = "doStuff();";

        var match = EditFileTool.TryWhitespaceNormalizedMatch(content, oldString);

        Assert.Equal("    doStuff();", match);
    }

    [Fact]
    public void TryWhitespaceNormalizedMatch_MultiLine()
    {
        var content = "fn() {\n    if (x) {\n        y();\n    }\n}";
        var oldString = "if (x) {\n    y();\n}";

        var match = EditFileTool.TryWhitespaceNormalizedMatch(content, oldString);

        Assert.Equal("    if (x) {\n        y();\n    }", match);
    }

    [Fact]
    public void TryWhitespaceNormalizedMatch_ReturnsNull_WhenAlreadyExact()
    {
        var content = "doStuff();";
        var oldString = "doStuff();";

        // No fallback needed — exact match exists, so helper returns null
        var match = EditFileTool.TryWhitespaceNormalizedMatch(content, oldString);

        Assert.Null(match);
    }

    [Fact]
    public void TryWhitespaceNormalizedMatch_ReturnsNull_WhenAmbiguous()
    {
        var content = "    doStuff();\n    doStuff();";
        var oldString = "doStuff();";

        var match = EditFileTool.TryWhitespaceNormalizedMatch(content, oldString);

        Assert.Null(match);
    }

    [Fact]
    public void TryWhitespaceNormalizedMatch_ReturnsNull_WhenNoMatch()
    {
        var content = "    doOther();";
        var oldString = "doStuff();";

        var match = EditFileTool.TryWhitespaceNormalizedMatch(content, oldString);

        Assert.Null(match);
    }

    [Fact]
    public void AdjustIndentation_AddsIndentDelta()
    {
        var newString = "if (false) {\n    doOther();\n}";
        var matchedOld = "    if (true) {\n        doStuff();\n    }";
        var originalOld = "if (true) {\n    doStuff();\n}";

        var result = EditFileTool.AdjustIndentation(newString, matchedOld, originalOld);

        Assert.Equal("    if (false) {\n        doOther();\n    }", result);
    }

    [Fact]
    public void AdjustIndentation_NoChangeWhenAlreadyIndented()
    {
        var newString = "        doStuff();";
        var matchedOld = "    doStuff();";
        var originalOld = "    doStuff();";

        var result = EditFileTool.AdjustIndentation(newString, matchedOld, originalOld);

        // old already had same or more indent than matched — no adjustment
        Assert.Equal("        doStuff();", result);
    }
}
