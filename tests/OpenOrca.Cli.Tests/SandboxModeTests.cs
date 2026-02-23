using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Cli.Rendering;
using OpenOrca.Cli.Repl;
using OpenOrca.Core.Configuration;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Registry;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class SandboxModeTests
{
    private ToolCallExecutor CreateExecutor(OrcaConfig config)
    {
        var logger = NullLogger<ToolRegistry>.Instance;
        var registry = new ToolRegistry(logger);

        // Register mock tools with different risk levels
        registry.Register(new FakeReadOnlyTool());
        registry.Register(new FakeModerateTool());
        registry.Register(new FakeDangerousTool());

        var state = new ReplState();
        var renderer = new ToolCallRenderer(state);
        return new ToolCallExecutor(registry, renderer, state, config, NullLogger<ToolCallExecutor>.Instance);
    }

    [Fact]
    public void IsToolAllowedInSandbox_ReadOnlyTool_ReturnsTrue()
    {
        var config = new OrcaConfig { SandboxMode = true };
        var executor = CreateExecutor(config);

        Assert.True(executor.IsToolAllowedInSandbox("fake_read"));
    }

    [Fact]
    public void IsToolAllowedInSandbox_ModerateTool_ReturnsFalse()
    {
        var config = new OrcaConfig { SandboxMode = true };
        var executor = CreateExecutor(config);

        Assert.False(executor.IsToolAllowedInSandbox("fake_moderate"));
    }

    [Fact]
    public void IsToolAllowedInSandbox_DangerousTool_ReturnsFalse()
    {
        var config = new OrcaConfig { SandboxMode = true };
        var executor = CreateExecutor(config);

        Assert.False(executor.IsToolAllowedInSandbox("fake_dangerous"));
    }

    [Fact]
    public void IsToolAllowedInSandbox_UnknownTool_ReturnsFalse()
    {
        var config = new OrcaConfig { SandboxMode = true };
        var executor = CreateExecutor(config);

        Assert.False(executor.IsToolAllowedInSandbox("nonexistent_tool"));
    }

    [Fact]
    public void GetToolsForMode_SandboxMode_OnlyReadOnlyTools()
    {
        var config = new OrcaConfig { SandboxMode = true };
        var executor = CreateExecutor(config);

        var logger = NullLogger<ToolRegistry>.Instance;
        var registry = new ToolRegistry(logger);
        registry.Register(new FakeReadOnlyTool());
        registry.Register(new FakeModerateTool());
        registry.Register(new FakeDangerousTool());

        executor.Tools = registry.GenerateAITools();
        var filtered = executor.GetToolsForMode();

        // Only the ReadOnly tool should be included
        Assert.Single(filtered);
    }

    [Fact]
    public void GetToolsForMode_NormalMode_AllTools()
    {
        var config = new OrcaConfig { SandboxMode = false };
        var executor = CreateExecutor(config);

        var logger = NullLogger<ToolRegistry>.Instance;
        var registry = new ToolRegistry(logger);
        registry.Register(new FakeReadOnlyTool());
        registry.Register(new FakeModerateTool());
        registry.Register(new FakeDangerousTool());

        executor.Tools = registry.GenerateAITools();
        var filtered = executor.GetToolsForMode();

        Assert.Equal(3, filtered.Count);
    }

    // ── Fake tools for testing ──

    private sealed class FakeReadOnlyTool : IOrcaTool
    {
        public string Name => "fake_read";
        public string Description => "Fake read-only tool";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;
        public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Success("ok"));
    }

    private sealed class FakeModerateTool : IOrcaTool
    {
        public string Name => "fake_moderate";
        public string Description => "Fake moderate tool";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;
        public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Success("ok"));
    }

    private sealed class FakeDangerousTool : IOrcaTool
    {
        public string Name => "fake_dangerous";
        public string Description => "Fake dangerous tool";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;
        public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Success("ok"));
    }
}
