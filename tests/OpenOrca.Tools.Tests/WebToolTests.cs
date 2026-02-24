using OpenOrca.Tools.Web;
using Xunit;
using static OpenOrca.Tools.Tests.TestHelpers;

namespace OpenOrca.Tools.Tests;

public class WebToolTests
{
    // ── WebFetchTool ──

    [Fact]
    public async Task WebFetchTool_RejectsInvalidUrl()
    {
        var tool = new WebFetchTool();
        var args = MakeArgs("""{"url": "not-a-url"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Invalid URL", result.Content);
    }

    [Fact]
    public async Task WebFetchTool_RejectsFtpScheme()
    {
        var tool = new WebFetchTool();
        var args = MakeArgs("""{"url": "ftp://example.com/file.txt"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Invalid URL", result.Content);
    }

    [Fact]
    public async Task WebFetchTool_FetchesExampleDotCom()
    {
        var tool = new WebFetchTool();
        var args = MakeArgs("""{"url": "https://example.com"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("Example Domain", result.Content);
    }

    [Fact]
    public async Task WebFetchTool_RespectsMaxLength()
    {
        var tool = new WebFetchTool();
        var args = MakeArgs("""{"url": "https://example.com", "max_length": 100}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("truncated", result.Content);
    }

    // ── WebSearchTool ──

    [Fact]
    public async Task WebSearchTool_SearchesSuccessfully()
    {
        var tool = new WebSearchTool();
        var args = MakeArgs("""{"query": "OpenAI API documentation", "max_results": 3}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // Should succeed (DuckDuckGo may or may not return results, but shouldn't error)
        Assert.False(result.IsError);
    }
}
