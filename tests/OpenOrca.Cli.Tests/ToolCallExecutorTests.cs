using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Cli.Rendering;
using OpenOrca.Cli.Repl;
using OpenOrca.Core.Chat;
using OpenOrca.Tools.Registry;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class ToolCallExecutorTests
{
    private static ToolCallExecutor CreateExecutor(ReplState? state = null, OpenOrca.Core.Configuration.OrcaConfig? config = null)
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);

        var renderer = new ToolCallRenderer();
        state ??= new ReplState();
        config ??= new OpenOrca.Core.Configuration.OrcaConfig();

        return new ToolCallExecutor(registry, renderer, state, config, NullLogger.Instance);
    }

    private static FunctionCallContent MakeCall(string name, string callId, Dictionary<string, object?>? args = null)
    {
        return new FunctionCallContent(callId, name, args);
    }

    // ── Plan mode filtering ──

    [Fact]
    public void IsToolAllowedInPlanMode_ReadOnlyTool_ReturnsTrue()
    {
        var executor = CreateExecutor();
        Assert.True(executor.IsToolAllowedInPlanMode("read_file"));
    }

    [Fact]
    public void IsToolAllowedInPlanMode_ModerateTool_ReturnsFalse()
    {
        var executor = CreateExecutor();
        Assert.False(executor.IsToolAllowedInPlanMode("write_file"));
    }

    [Fact]
    public void IsToolAllowedInPlanMode_DangerousTool_ReturnsFalse()
    {
        var executor = CreateExecutor();
        Assert.False(executor.IsToolAllowedInPlanMode("bash"));
    }

    [Fact]
    public void IsToolAllowedInPlanMode_UnknownTool_ReturnsFalse()
    {
        var executor = CreateExecutor();
        Assert.False(executor.IsToolAllowedInPlanMode("nonexistent_tool"));
    }

    // ── GetToolsForMode ──

    [Fact]
    public void GetToolsForMode_NormalMode_ReturnsAllTools()
    {
        var state = new ReplState { PlanMode = false };
        var executor = CreateExecutor(state);

        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);
        executor.Tools = registry.GenerateAITools();

        var tools = executor.GetToolsForMode();
        Assert.Equal(executor.Tools.Count, tools.Count);
    }

    [Fact]
    public void GetToolsForMode_PlanMode_FiltersNonReadOnly()
    {
        var state = new ReplState { PlanMode = true };
        var executor = CreateExecutor(state);

        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);
        executor.Tools = registry.GenerateAITools();

        var tools = executor.GetToolsForMode();
        Assert.True(tools.Count < executor.Tools.Count);
        Assert.True(tools.Count > 0);
    }

    [Fact]
    public void GetToolsForMode_NullTools_ReturnsEmpty()
    {
        var executor = CreateExecutor();
        executor.Tools = null;

        var tools = executor.GetToolsForMode();
        Assert.Empty(tools);
    }

    // ── Error tracking ──

    [Fact]
    public void ClearRecentErrors_ResetsState()
    {
        var executor = CreateExecutor();
        executor.ClearRecentErrors();

        var (_, count) = executor.GetMaxFailure();
        Assert.Equal(0, count);
    }

    [Fact]
    public void GetMaxFailure_NoErrors_ReturnsZeroCount()
    {
        var executor = CreateExecutor();
        var (_, count) = executor.GetMaxFailure();
        Assert.Equal(0, count);
    }

    // ── Parallel execution ──

    [Fact]
    public async Task ExecuteToolCallsAsync_RunsToolsInParallel()
    {
        var executor = CreateExecutor();
        var conversation = new Conversation();

        // Each tool call delays 200ms — if parallel, total should be ~200ms not ~600ms
        executor.ToolExecutor = async (name, args, ct) =>
        {
            await Task.Delay(200, ct);
            return $"result-{name}";
        };

        var calls = new List<FunctionCallContent>
        {
            MakeCall("tool_a", "call-1"),
            MakeCall("tool_b", "call-2"),
            MakeCall("tool_c", "call-3"),
        };

        var sw = Stopwatch.StartNew();
        await executor.ExecuteToolCallsAsync(calls, conversation, CancellationToken.None);
        sw.Stop();

        // Parallel: ~200ms. Sequential would be ~600ms. Allow very generous margin for CI runners.
        Assert.True(sw.ElapsedMilliseconds < 1500,
            $"Expected parallel execution under 1500ms, but took {sw.ElapsedMilliseconds}ms");

        // Should have 3 tool result messages
        Assert.Equal(3, conversation.Messages.Count);
    }

    [Fact]
    public async Task ExecuteToolCallsAsync_PreservesResultOrder()
    {
        var executor = CreateExecutor();
        var conversation = new Conversation();

        // Tool B finishes first, Tool A finishes last — results should still be in A, B, C order
        executor.ToolExecutor = async (name, args, ct) =>
        {
            var delay = name switch
            {
                "tool_a" => 150,
                "tool_b" => 10,
                "tool_c" => 80,
                _ => 0
            };
            await Task.Delay(delay, ct);
            return $"result-{name}";
        };

        var calls = new List<FunctionCallContent>
        {
            MakeCall("tool_a", "call-a"),
            MakeCall("tool_b", "call-b"),
            MakeCall("tool_c", "call-c"),
        };

        await executor.ExecuteToolCallsAsync(calls, conversation, CancellationToken.None);

        Assert.Equal(3, conversation.Messages.Count);

        // Verify order: A, B, C
        var resultA = conversation.Messages[0].Contents.OfType<FunctionResultContent>().Single();
        var resultB = conversation.Messages[1].Contents.OfType<FunctionResultContent>().Single();
        var resultC = conversation.Messages[2].Contents.OfType<FunctionResultContent>().Single();

        Assert.Equal("call-a", resultA.CallId);
        Assert.Equal("call-b", resultB.CallId);
        Assert.Equal("call-c", resultC.CallId);

        Assert.Contains("result-tool_a", resultA.Result?.ToString());
        Assert.Contains("result-tool_b", resultB.Result?.ToString());
        Assert.Contains("result-tool_c", resultC.Result?.ToString());
    }

    [Fact]
    public async Task ExecuteToolCallsAsync_ErrorIsolation_OtherToolsSucceed()
    {
        var executor = CreateExecutor();
        var conversation = new Conversation();

        // Tool B throws, A and C should still succeed
        executor.ToolExecutor = async (name, args, ct) =>
        {
            await Task.Delay(10, ct);
            if (name == "tool_b")
                throw new InvalidOperationException("tool_b exploded");
            return $"result-{name}";
        };

        var calls = new List<FunctionCallContent>
        {
            MakeCall("tool_a", "call-a"),
            MakeCall("tool_b", "call-b"),
            MakeCall("tool_c", "call-c"),
        };

        await executor.ExecuteToolCallsAsync(calls, conversation, CancellationToken.None);

        Assert.Equal(3, conversation.Messages.Count);

        var resultA = conversation.Messages[0].Contents.OfType<FunctionResultContent>().Single();
        var resultB = conversation.Messages[1].Contents.OfType<FunctionResultContent>().Single();
        var resultC = conversation.Messages[2].Contents.OfType<FunctionResultContent>().Single();

        Assert.Contains("result-tool_a", resultA.Result?.ToString());
        Assert.Contains("Error: tool_b exploded", resultB.Result?.ToString());
        Assert.Contains("result-tool_c", resultC.Result?.ToString());
    }

    [Fact]
    public async Task ExecuteTextToolCallsAsync_RunsToolsInParallel()
    {
        var executor = CreateExecutor();
        var conversation = new Conversation();

        executor.ToolExecutor = async (name, args, ct) =>
        {
            await Task.Delay(200, ct);
            return $"result-{name}";
        };

        var calls = new List<FunctionCallContent>
        {
            MakeCall("tool_a", "call-1"),
            MakeCall("tool_b", "call-2"),
            MakeCall("tool_c", "call-3"),
        };

        var sw = Stopwatch.StartNew();
        await executor.ExecuteTextToolCallsAsync(calls, conversation, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1500,
            $"Expected parallel execution under 1500ms, but took {sw.ElapsedMilliseconds}ms");

        // Text mode: results injected as a single user message
        Assert.Single(conversation.Messages);
        var msg = conversation.Messages[0];
        Assert.Equal(ChatRole.User, msg.Role);

        var text = msg.Contents.OfType<TextContent>().Single().Text!;
        Assert.Contains("result-tool_a", text);
        Assert.Contains("result-tool_b", text);
        Assert.Contains("result-tool_c", text);
    }
}
