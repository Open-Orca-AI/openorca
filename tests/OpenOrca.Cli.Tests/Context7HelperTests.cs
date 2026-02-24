using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Cli.Repl;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Registry;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class Context7HelperTests
{
    private static ToolRegistry CreateRegistry(params IOrcaTool[] tools)
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        foreach (var tool in tools)
            registry.Register(tool);
        return registry;
    }

    [Fact]
    public void IsAvailable_NeitherTool_ReturnsFalse()
    {
        var helper = new Context7Helper(CreateRegistry());
        Assert.False(helper.IsAvailable());
    }

    [Fact]
    public void IsAvailable_OnlyResolveTool_ReturnsFalse()
    {
        var resolve = new FakeTool("mcp_resolve-library-id");
        var helper = new Context7Helper(CreateRegistry(resolve));
        Assert.False(helper.IsAvailable());
    }

    [Fact]
    public void IsAvailable_BothTools_ReturnsTrue()
    {
        var resolve = new FakeTool("mcp_resolve-library-id");
        var query = new FakeTool("mcp_get-library-docs");
        var helper = new Context7Helper(CreateRegistry(resolve, query));
        Assert.True(helper.IsAvailable());
    }

    [Fact]
    public void IsAvailable_QueryDocsVariant_ReturnsTrue()
    {
        var resolve = new FakeTool("mcp_resolve-library-id");
        var query = new FakeTool("mcp_query-docs");
        var helper = new Context7Helper(CreateRegistry(resolve, query));
        Assert.True(helper.IsAvailable());
    }

    [Fact]
    public async Task ResolveLibraryIdAsync_Success_ReturnsId()
    {
        var resolve = new FakeTool("mcp_resolve-library-id", "  /reactjs/react  ");
        var query = new FakeTool("mcp_get-library-docs");
        var helper = new Context7Helper(CreateRegistry(resolve, query));

        var result = await helper.ResolveLibraryIdAsync("react");
        Assert.Equal("/reactjs/react", result);
    }

    [Fact]
    public async Task ResolveLibraryIdAsync_Error_ReturnsNull()
    {
        var resolve = new FakeTool("mcp_resolve-library-id", error: true);
        var query = new FakeTool("mcp_get-library-docs");
        var helper = new Context7Helper(CreateRegistry(resolve, query));

        var result = await helper.ResolveLibraryIdAsync("unknown-lib");
        Assert.Null(result);
    }

    [Fact]
    public async Task QueryDocsAsync_Success_ReturnsDocs()
    {
        var resolve = new FakeTool("mcp_resolve-library-id");
        var query = new FakeTool("mcp_get-library-docs", "# React Hooks\nUse hooks for state.");
        var helper = new Context7Helper(CreateRegistry(resolve, query));

        var result = await helper.QueryDocsAsync("/reactjs/react", "hooks");
        Assert.Equal("# React Hooks\nUse hooks for state.", result);
    }

    [Fact]
    public async Task FetchDocsAsync_ResolveFails_ReturnsNull()
    {
        var resolve = new FakeTool("mcp_resolve-library-id", error: true);
        var query = new FakeTool("mcp_get-library-docs", "docs content");
        var helper = new Context7Helper(CreateRegistry(resolve, query));

        var result = await helper.FetchDocsAsync("unknown-lib", "query");
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchDocsAsync_BothSucceed_ReturnsDocs()
    {
        var resolve = new FakeTool("mcp_resolve-library-id", "/reactjs/react");
        var query = new FakeTool("mcp_get-library-docs", "# React Docs");
        var helper = new Context7Helper(CreateRegistry(resolve, query));

        var result = await helper.FetchDocsAsync("react", "hooks");
        Assert.Equal("# React Docs", result);
    }

    [Fact]
    public async Task ResolveLibraryIdAsync_NoToolAvailable_ReturnsNull()
    {
        var helper = new Context7Helper(CreateRegistry());
        var result = await helper.ResolveLibraryIdAsync("react");
        Assert.Null(result);
    }

    /// <summary>
    /// Minimal fake IOrcaTool for testing Context7Helper without MCP dependencies.
    /// </summary>
    private sealed class FakeTool : IOrcaTool
    {
        private readonly string? _response;
        private readonly bool _error;

        public FakeTool(string name, string? response = null, bool error = false)
        {
            Name = name;
            _response = response;
            _error = error;
        }

        public string Name { get; }
        public string Description => "Fake tool for testing";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;

        public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
        {
            if (_error)
                return Task.FromResult(ToolResult.Error("Tool error"));

            return Task.FromResult(ToolResult.Success(_response ?? ""));
        }
    }
}
