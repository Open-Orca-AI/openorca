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

    private static (ToolCallRenderer renderer, ReplState state, TestConsole console) CreateWithAnsi()
    {
        var state = new ReplState();
        var console = new TestConsole();
        console.EmitAnsiSequences = true;
        var renderer = new ToolCallRenderer(state, console);
        return (renderer, state, console);
    }

    [Fact]
    public void RenderToolResult_EditFile_RemovedLines_HaveRedBackground()
    {
        var (renderer, state, console) = CreateWithAnsi();
        state.Verbosity = 2; // full output
        var result = "Replaced 1 occurrence(s) in file.cs\n\n--- diff ---\n     1   context\n     2 - old line\n     2 + new line\n     3   context";

        renderer.RenderToolResult("edit_file", result);
        var output = console.Output;

        // ANSI 24-bit background for #3d0000 (RGB 61,0,0): ESC[48;2;61;0;0m
        Assert.Contains("48;2;61;0;0", output);
    }

    [Fact]
    public void RenderToolResult_EditFile_AddedLines_HaveGreenBackground()
    {
        var (renderer, state, console) = CreateWithAnsi();
        state.Verbosity = 2;
        var result = "Replaced 1 occurrence(s) in file.cs\n\n--- diff ---\n     1   context\n     2 - old line\n     2 + new line\n     3   context";

        renderer.RenderToolResult("edit_file", result);
        var output = console.Output;

        // ANSI 24-bit background for #003d00 (RGB 0,61,0): ESC[48;2;0;61;0m
        Assert.Contains("48;2;0;61;0", output);
    }

    [Fact]
    public void RenderToolResult_EditFile_ContextLines_AreDim()
    {
        var (renderer, state, console) = CreateWithAnsi();
        state.Verbosity = 2;
        var result = "--- diff ---\n     1   context line\n     2 - old\n     2 + new";

        renderer.RenderToolResult("edit_file", result);
        var output = console.Output;

        // ANSI dim: ESC[2m
        Assert.Contains("\x1b[2m", output);
    }

    [Fact]
    public void RenderToolResult_EditFile_PreviewMode_ShowsAddedLines()
    {
        var (renderer, state, console) = CreateWithAnsi();
        state.Verbosity = 1; // default preview mode
        // Build a result where header + context + removal lines exceed the 7-line budget,
        // pushing addition lines past the preview limit.
        var result = "Replaced 1 occurrence(s) in file.cs\n\n--- diff ---\n     1   ctx1\n     2   ctx2\n     3   ctx3\n     4 - old line\n     4 + new line\n     5   ctx4";

        renderer.RenderToolResult("edit_file", result);
        var output = console.Output;

        // Addition line must appear with green background even at default verbosity
        Assert.Contains("48;2;0;61;0", output);
    }

    [Fact]
    public void RenderToolResult_EditFile_PreviewMode_PinsTwoContextLinesAroundChange()
    {
        var (renderer, state, console) = CreateWithAnsi();
        state.Verbosity = 1;
        // 10 context lines before, then a change, then 2 context after.
        // Budget is 7 — without pinning, context around the change gets truncated.
        var result = string.Join("\n",
            "--- diff ---",
            "     1   ctx1", "     2   ctx2", "     3   ctx3", "     4   ctx4",
            "     5   ctx5", "     6   ctx6", "     7   ctx7", "     8   ctx8",
            "     9   ctx9", "    10   ctx10",
            "    11 - old", "    11 + new",
            "    12   after1", "    13   after2");

        renderer.RenderToolResult("edit_file", result);
        var output = console.Output;

        // The 2 context lines immediately before the change must be pinned
        Assert.Contains("ctx9", output);
        Assert.Contains("ctx10", output);
        // The 2 context lines immediately after must be pinned
        Assert.Contains("after1", output);
        Assert.Contains("after2", output);
        // Lines between budget and pinned region should be truncated
        Assert.DoesNotContain("ctx7", output);
        Assert.DoesNotContain("ctx8", output);
    }

    [Fact]
    public void RenderToolResult_EditFile_PreviewMode_BlankSeparatorBetweenNonAdjacentBlocks()
    {
        var (renderer, state, console) = CreateWithAnsi();
        state.Verbosity = 1;
        // Two change blocks separated by many context lines (12 unpinned mid-lines).
        // Budget of 7 can't show them all, so skipped lines produce a blank separator.
        var midLines = Enumerable.Range(4, 12).Select(n => $"    {n,2}   mid{n - 3}");
        var result = string.Join("\n",
            new[] { "     1   before1", "     2   before2",
                    "     3 - oldA", "     3 + newA" }
            .Concat(midLines)
            .Concat(new[] { "    16 - oldB", "    16 + newB",
                            "    17   after1", "    18   after2" }));

        renderer.RenderToolResult("edit_file", result);
        var output = console.Output;

        // Both change blocks must appear
        Assert.Contains("oldA", output);
        Assert.Contains("newA", output);
        Assert.Contains("oldB", output);
        Assert.Contains("newB", output);

        // There should be a blank separator between the blocks (accounts for \r\n on Windows)
        var normalized = output.Replace("\r\n", "\n");
        Assert.Contains("\n\n", normalized);
    }

    [Fact]
    public void RenderToolResult_EditFile_PreviewMode_AdjacentBlocksShareContext()
    {
        var (renderer, state, console) = CreateWithAnsi();
        state.Verbosity = 1;
        // Two change blocks only 2 lines apart — context regions overlap, no blank separator.
        var result = string.Join("\n",
            "     1   before",
            "     2 - oldA", "     2 + newA",
            "     3   shared1", "     4   shared2",
            "     5 - oldB", "     5 + newB",
            "     6   after");

        renderer.RenderToolResult("edit_file", result);
        var output = console.Output;

        // All lines should be shown (they're all within 2 of a change)
        Assert.Contains("before", output);
        Assert.Contains("oldA", output);
        Assert.Contains("newA", output);
        Assert.Contains("shared1", output);
        Assert.Contains("shared2", output);
        Assert.Contains("oldB", output);
        Assert.Contains("newB", output);
        Assert.Contains("after", output);

        // No blank separator since the blocks are adjacent
        var normalized = output.Replace("\r\n", "\n");
        Assert.DoesNotContain("\n\n", normalized);
    }
}
