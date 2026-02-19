using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Cli.Rendering;
using OpenOrca.Cli.Repl;
using OpenOrca.Tools.Registry;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class ToolCallExecutorTests
{
    private static ToolCallExecutor CreateExecutor(ReplState? state = null)
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);

        var renderer = new ToolCallRenderer();
        state ??= new ReplState();

        return new ToolCallExecutor(registry, renderer, state, NullLogger.Instance);
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
}
