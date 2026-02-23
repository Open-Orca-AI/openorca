using OpenOrca.Cli.Rendering;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class ToolCallTagFilterTests
{
    [Fact]
    public void PlainText_PassesThrough()
    {
        var filter = new ToolCallTagFilter();
        Assert.Equal("Hello world", filter.Filter("Hello world"));
    }

    [Fact]
    public void EmptyString_PassesThrough()
    {
        var filter = new ToolCallTagFilter();
        Assert.Equal("", filter.Filter(""));
    }

    [Fact]
    public void NullString_PassesThrough()
    {
        var filter = new ToolCallTagFilter();
        Assert.Null(filter.Filter(null!));
    }

    [Fact]
    public void ToolCallTag_Suppressed_SingleToken()
    {
        var filter = new ToolCallTagFilter();
        var result = filter.Filter("<tool_call>{\"name\":\"read_file\"}</tool_call>");
        Assert.Equal("", result);
    }

    [Fact]
    public void PipeToolCallTag_Suppressed_SingleToken()
    {
        var filter = new ToolCallTagFilter();
        var result = filter.Filter("<|tool_call|>{\"name\":\"read_file\"}<|/tool_call|>");
        Assert.Equal("", result);
    }

    [Fact]
    public void TextBeforeToolCall_Preserved()
    {
        var filter = new ToolCallTagFilter();
        var result = filter.Filter("Here is my plan: <tool_call>{\"name\":\"bash\"}</tool_call>");
        Assert.Equal("Here is my plan: ", result);
    }

    [Fact]
    public void TextAfterToolCall_Preserved()
    {
        var filter = new ToolCallTagFilter();
        var result = filter.Filter("<tool_call>{\"name\":\"bash\"}</tool_call>Done!");
        Assert.Equal("Done!", result);
    }

    [Fact]
    public void TextAroundToolCall_Preserved()
    {
        var filter = new ToolCallTagFilter();
        var result = filter.Filter("Before <tool_call>{\"name\":\"bash\"}</tool_call> After");
        Assert.Equal("Before  After", result);
    }

    [Fact]
    public void ToolCallTag_Suppressed_TokenByToken()
    {
        var filter = new ToolCallTagFilter();
        var output = "";
        output += filter.Filter("<");
        output += filter.Filter("tool_call");
        output += filter.Filter(">");
        output += filter.Filter("{\"name\":\"read_file\"}");
        output += filter.Filter("</tool_call>");
        Assert.Equal("", output);
    }

    [Fact]
    public void ToolCallTag_Suppressed_CharByChar()
    {
        var filter = new ToolCallTagFilter();
        var input = "<tool_call>{\"name\":\"x\"}</tool_call>";
        var output = "";
        foreach (var ch in input)
            output += filter.Filter(ch.ToString());
        Assert.Equal("", output);
    }

    [Fact]
    public void MixedTextAndToolCall_TokenByToken()
    {
        var filter = new ToolCallTagFilter();
        var output = "";
        output += filter.Filter("I will ");
        output += filter.Filter("do this: ");
        output += filter.Filter("<tool_call>");
        output += filter.Filter("{\"name\":\"bash\",\"arguments\":{\"command\":\"ls\"}}");
        output += filter.Filter("</tool_call>");
        output += filter.Filter(" and done");
        Assert.Equal("I will do this:  and done", output);
    }

    [Fact]
    public void MultipleToolCalls_AllSuppressed()
    {
        var filter = new ToolCallTagFilter();
        var output = "";
        output += filter.Filter("A <tool_call>{\"name\":\"a\"}</tool_call>");
        output += filter.Filter(" B <tool_call>{\"name\":\"b\"}</tool_call> C");
        Assert.Equal("A  B  C", output);
    }

    [Fact]
    public void NonToolCallAngleBracket_Preserved()
    {
        var filter = new ToolCallTagFilter();
        // '<' followed by non-matching text should flush
        var output = "";
        output += filter.Filter("x < y");
        output += filter.Filter(" and ");
        output += filter.Filter("z > w");
        Assert.Equal("x < y and z > w", output);
    }

    [Fact]
    public void HtmlTags_Preserved()
    {
        var filter = new ToolCallTagFilter();
        Assert.Equal("<div>hello</div>", filter.Filter("<div>hello</div>"));
    }

    [Fact]
    public void CaseInsensitive_ToolCallTag()
    {
        var filter = new ToolCallTagFilter();
        var result = filter.Filter("<TOOL_CALL>{\"name\":\"x\"}</TOOL_CALL>");
        Assert.Equal("", result);
    }

    [Fact]
    public void PipeTag_TokenByToken()
    {
        var filter = new ToolCallTagFilter();
        var output = "";
        output += filter.Filter("<|");
        output += filter.Filter("tool_call|>");
        output += filter.Filter("{\"name\":\"x\"}");
        output += filter.Filter("<|/tool_call|>");
        Assert.Equal("", output);
    }

    [Fact]
    public void SplitCloseTag_AcrossTokens()
    {
        var filter = new ToolCallTagFilter();
        var output = "";
        output += filter.Filter("<tool_call>{\"name\":\"x\"}</");
        output += filter.Filter("tool_call>");
        output += filter.Filter("visible");
        Assert.Equal("visible", output);
    }

    [Fact]
    public void TextWithMultipleAngleBrackets_Preserved()
    {
        var filter = new ToolCallTagFilter();
        var result = filter.Filter("if (a < b && c > d) return true;");
        Assert.Equal("if (a < b && c > d) return true;", result);
    }
}
