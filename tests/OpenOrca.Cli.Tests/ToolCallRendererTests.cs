using OpenOrca.Cli.Rendering;
using OpenOrca.Cli.Repl;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class ToolCallRendererTests
{
    private static (ToolCallRenderer renderer, ReplState state, TestConsole console) Create()
    {
        var state = new ReplState();
        var console = new TestConsole();
        var renderer = new ToolCallRenderer(state, console);
        return (renderer, state, console);
    }

    [Fact]
    public void RenderToolResult_Success_FewLines_ShowsAllNoTruncation()
    {
        var (renderer, _, console) = Create();
        var result = "line1\nline2\nline3";

        renderer.RenderToolResult("read_file", result);
        var output = console.Output;

        Assert.Contains("line1", output);
        Assert.Contains("line2", output);
        Assert.Contains("line3", output);
        Assert.DoesNotContain("more line", output);
    }

    [Fact]
    public void RenderToolResult_Success_ManyLines_TruncatesWithHint()
    {
        var (renderer, _, console) = Create();
        var lines = Enumerable.Range(1, 12).Select(i => $"line{i}");
        var result = string.Join("\n", lines);

        renderer.RenderToolResult("read_file", result);
        var output = console.Output;

        // First 7 lines shown
        for (var i = 1; i <= 7; i++)
            Assert.Contains($"line{i}", output);

        // Truncation hint
        Assert.Contains("5 more lines", output);
        Assert.Contains("Ctrl+O to increase verbosity", output);
    }

    [Fact]
    public void RenderToolResult_Success_ManyLines_ShowThinking_ShowsAll()
    {
        var (renderer, state, console) = Create();
        state.Verbosity = 2; // ShowFullToolOutput
        var lines = Enumerable.Range(1, 12).Select(i => $"line{i}");
        var result = string.Join("\n", lines);

        renderer.RenderToolResult("read_file", result);
        var output = console.Output;

        // All 12 lines shown
        for (var i = 1; i <= 12; i++)
            Assert.Contains($"line{i}", output);

        // No truncation hint
        Assert.DoesNotContain("more line", output);
    }

    [Fact]
    public void RenderToolResult_Error_ShowsRedHeaderAndBodyPreview()
    {
        var (renderer, _, console) = Create();
        var result = "File not found\ndetail line 1\ndetail line 2";

        renderer.RenderToolResult("write_file", result, isError: true);
        var output = console.Output;

        Assert.Contains("write_file", output);
        Assert.Contains("File not found", output);
        Assert.Contains("detail line 1", output);
        Assert.Contains("detail line 2", output);
    }

    [Fact]
    public void RenderToolResult_EmptyResult_NoOutput()
    {
        var (renderer, _, console) = Create();

        renderer.RenderToolResult("read_file", "");
        Assert.Empty(console.Output);
    }

    [Fact]
    public void RenderToolResult_WhitespaceResult_NoOutput()
    {
        var (renderer, _, console) = Create();

        renderer.RenderToolResult("read_file", "   \n  ");
        Assert.Empty(console.Output);
    }

    [Fact]
    public void RenderToolResult_Suppressed_NoOutput()
    {
        var (renderer, _, console) = Create();
        renderer.Suppressed = true;

        renderer.RenderToolResult("read_file", "some content");
        Assert.Empty(console.Output);
    }

    [Fact]
    public void RenderToolResult_ExactlyMaxLines_NoTruncation()
    {
        var (renderer, _, console) = Create();
        var lines = Enumerable.Range(1, CliConstants.ToolOutputPreviewLines).Select(i => $"line{i}");
        var result = string.Join("\n", lines);

        renderer.RenderToolResult("read_file", result);
        var output = console.Output;

        for (var i = 1; i <= CliConstants.ToolOutputPreviewLines; i++)
            Assert.Contains($"line{i}", output);

        Assert.DoesNotContain("more line", output);
    }

    [Fact]
    public void RenderToolResult_SingleMoreLine_SingularHint()
    {
        var (renderer, _, console) = Create();
        // 8 lines = 7 shown + 1 more
        var lines = Enumerable.Range(1, CliConstants.ToolOutputPreviewLines + 1).Select(i => $"line{i}");
        var result = string.Join("\n", lines);

        renderer.RenderToolResult("read_file", result);
        var output = console.Output;

        Assert.Contains("1 more line ", output); // trailing space ensures "line" not "lines"
    }
}
