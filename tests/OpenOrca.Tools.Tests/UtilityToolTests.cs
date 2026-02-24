using OpenOrca.Tools.Utilities;
using OpenOrca.Tools.UtilityTools;
using Xunit;
using static OpenOrca.Tools.Tests.TestHelpers;

namespace OpenOrca.Tools.Tests;

public class UtilityToolTests : IDisposable
{
    public UtilityToolTests()
    {
        TaskStore.Reset();
    }

    public void Dispose()
    {
        TaskStore.Reset();
    }

    // ── ThinkTool ──

    [Fact]
    public async Task ThinkTool_ReturnsThoughtVerbatim()
    {
        var tool = new ThinkTool();
        var thought = "I need to: 1) read the file 2) find the bug 3) fix it";
        var args = MakeArgs($$"""{"thought": "{{thought}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(thought, result.Content);
    }

    [Fact]
    public async Task ThinkTool_HandlesMultilineThoughts()
    {
        var tool = new ThinkTool();
        var args = MakeArgs("""{"thought": "Line1\nLine2\nLine3"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("Line1", result.Content);
        Assert.Contains("Line3", result.Content);
    }

    // ── TaskListTool ──

    [Fact]
    public async Task TaskListTool_AddAndList()
    {
        var tool = new TaskListTool();

        var addResult = await tool.ExecuteAsync(
            MakeArgs("""{"action": "add", "task": "Write unit tests"}"""),
            CancellationToken.None);

        Assert.False(addResult.IsError);
        Assert.Contains("#1", addResult.Content);
        Assert.Contains("Write unit tests", addResult.Content);

        var listResult = await tool.ExecuteAsync(
            MakeArgs("""{"action": "list"}"""),
            CancellationToken.None);

        Assert.False(listResult.IsError);
        Assert.Contains("Write unit tests", listResult.Content);
        Assert.Contains("0/1 completed", listResult.Content);
    }

    [Fact]
    public async Task TaskListTool_CompleteTask()
    {
        var tool = new TaskListTool();

        await tool.ExecuteAsync(
            MakeArgs("""{"action": "add", "task": "Task A"}"""),
            CancellationToken.None);
        await tool.ExecuteAsync(
            MakeArgs("""{"action": "add", "task": "Task B"}"""),
            CancellationToken.None);

        var completeResult = await tool.ExecuteAsync(
            MakeArgs("""{"action": "complete", "id": 1}"""),
            CancellationToken.None);

        Assert.False(completeResult.IsError);
        Assert.Contains("1/2 completed", completeResult.Content);
        Assert.Contains("[x] #1", completeResult.Content);
        Assert.Contains("[ ] #2", completeResult.Content);
    }

    [Fact]
    public async Task TaskListTool_RemoveTask()
    {
        var tool = new TaskListTool();

        await tool.ExecuteAsync(
            MakeArgs("""{"action": "add", "task": "Temporary"}"""),
            CancellationToken.None);

        var removeResult = await tool.ExecuteAsync(
            MakeArgs("""{"action": "remove", "id": 1}"""),
            CancellationToken.None);

        Assert.False(removeResult.IsError);
        Assert.Contains("No tasks", removeResult.Content);
    }

    [Fact]
    public async Task TaskListTool_CompleteInvalidId_ReturnsError()
    {
        var tool = new TaskListTool();

        var result = await tool.ExecuteAsync(
            MakeArgs("""{"action": "complete", "id": 999}"""),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content);
    }

    [Fact]
    public async Task TaskListTool_AddWithoutDescription_ReturnsError()
    {
        var tool = new TaskListTool();

        var result = await tool.ExecuteAsync(
            MakeArgs("""{"action": "add"}"""),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("required", result.Content.ToLower());
    }

    [Fact]
    public async Task TaskListTool_EmptyList()
    {
        var tool = new TaskListTool();

        var result = await tool.ExecuteAsync(
            MakeArgs("""{"action": "list"}"""),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("No tasks", result.Content);
    }
}
