using OpenOrca.Tools.Interactive;
using Xunit;
using static OpenOrca.Tools.Tests.TestHelpers;

namespace OpenOrca.Tools.Tests;

public class AskUserToolTests
{
    private AskUserTool CreateTool()
    {
        var tool = new AskUserTool();
        tool.UserPrompter = (question, options, ct) => Task.FromResult(options[0]);
        return tool;
    }

    [Fact]
    public async Task AskUser_NormalArray_Works()
    {
        var tool = CreateTool();
        var args = MakeArgs("""{"question": "Pick one", "options": ["A", "B"]}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("A", result.Content);
    }

    [Fact]
    public async Task AskUser_StringifiedArray_ParsedSuccessfully()
    {
        var tool = CreateTool();
        // Model sends options as a JSON string containing an array (double-serialized)
        var args = MakeArgs("""{"question": "Pick one", "options": "[\"Option A\", \"Option B\", \"Option C\"]"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Option A", result.Content);
    }

    [Fact]
    public async Task AskUser_InvalidStringOptions_ReturnsError()
    {
        var tool = CreateTool();
        var args = MakeArgs("""{"question": "Pick one", "options": "not valid json"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("array", result.Content);
    }

    [Fact]
    public async Task AskUser_TooFewOptions_ReturnsError()
    {
        var tool = CreateTool();
        var args = MakeArgs("""{"question": "Pick one", "options": ["Only one"]}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("2 options", result.Content);
    }
}
