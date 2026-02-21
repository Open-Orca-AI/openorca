using OpenOrca.Cli.Rendering;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class DiffRendererTests
{
    [Fact]
    public void ColorizeDiffText_AddedLines_ContainsGreen()
    {
        var result = DiffRenderer.ColorizeDiffText("+added line");

        Assert.Contains("[green]", result);
        Assert.Contains("added line", result);
    }

    [Fact]
    public void ColorizeDiffText_RemovedLines_ContainsRed()
    {
        var result = DiffRenderer.ColorizeDiffText("-removed line");

        Assert.Contains("[red]", result);
        Assert.Contains("removed line", result);
    }

    [Fact]
    public void ColorizeDiffText_HunkHeaders_ContainsCyan()
    {
        var result = DiffRenderer.ColorizeDiffText("@@ -1,3 +1,4 @@");

        Assert.Contains("[cyan]", result);
    }

    [Fact]
    public void ColorizeDiffText_DiffHeaders_ContainsBoldYellow()
    {
        var result = DiffRenderer.ColorizeDiffText("diff --git a/file.cs b/file.cs");

        Assert.Contains("[bold yellow]", result);
    }

    [Fact]
    public void ColorizeDiffText_FileHeaders_ContainsBold()
    {
        var result = DiffRenderer.ColorizeDiffText("--- a/file.cs\n+++ b/file.cs");

        Assert.Contains("[bold]", result);
    }

    [Fact]
    public void ColorizeDiffText_ContextLines_ContainsDim()
    {
        var result = DiffRenderer.ColorizeDiffText(" unchanged context line");

        Assert.Contains("[dim]", result);
    }

    [Fact]
    public void ColorizeDiffText_EmptyDiff_ReturnsEmpty()
    {
        var result = DiffRenderer.ColorizeDiffText("");

        Assert.NotNull(result);
        Assert.Equal("[dim][/]", result);
    }

    [Fact]
    public void ColorizeDiffText_MultipleLineTypes_AllColorized()
    {
        var diff = "diff --git a/f.cs b/f.cs\n--- a/f.cs\n+++ b/f.cs\n@@ -1,2 +1,3 @@\n context\n-old\n+new";
        var result = DiffRenderer.ColorizeDiffText(diff);

        Assert.Contains("[bold yellow]", result); // diff header
        Assert.Contains("[bold]", result);         // --- / +++
        Assert.Contains("[cyan]", result);          // @@
        Assert.Contains("[green]", result);         // +
        Assert.Contains("[red]", result);           // -
        Assert.Contains("[dim]", result);           // context
    }

    [Fact]
    public void ColorizeDiffText_EscapesMarkupCharacters()
    {
        // Spectre.Console markup chars like [ ] should be escaped
        var result = DiffRenderer.ColorizeDiffText("+var x = list[0];");

        Assert.Contains("[green]", result);
        Assert.Contains("list[[0]]", result); // escaped brackets
    }
}
