using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Cli.Repl;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Registry;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class DocsPreprocessorTests
{
    private static ToolRegistry CreateRegistry(string? resolveResponse = "/lib/id", string? queryResponse = "# Docs content")
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.Register(new FakeTool("mcp_resolve-library-id", resolveResponse));
        registry.Register(new FakeTool("mcp_get-library-docs", queryResponse));
        return registry;
    }

    private static DocsPreprocessor CreatePreprocessor(string? resolveResponse = "/lib/id", string? queryResponse = "# Docs content")
    {
        var registry = CreateRegistry(resolveResponse, queryResponse);
        var helper = new Context7Helper(registry);
        return new DocsPreprocessor(helper);
    }

    [Fact]
    public async Task ExpandDocsReferences_NoDocsToken_ReturnsUnchanged()
    {
        var preprocessor = CreatePreprocessor();
        var input = "just a normal message";
        var result = await preprocessor.ExpandDocsReferencesAsync(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public async Task ExpandDocsReferences_EmptyInput_ReturnsUnchanged()
    {
        var preprocessor = CreatePreprocessor();
        var result = await preprocessor.ExpandDocsReferencesAsync("");
        Assert.Equal("", result);
    }

    [Fact]
    public async Task ExpandDocsReferences_SingleReference_ExpandsDocs()
    {
        var preprocessor = CreatePreprocessor();
        var result = await preprocessor.ExpandDocsReferencesAsync("explain @docs:react");

        Assert.Contains("[Documentation for react via Context7]", result);
        Assert.Contains("# Docs content", result);
        Assert.Contains("[/Documentation]", result);
        Assert.DoesNotContain("@docs:react", result);
    }

    [Fact]
    public async Task ExpandDocsReferences_MultipleReferences_AllExpanded()
    {
        var preprocessor = CreatePreprocessor();
        var result = await preprocessor.ExpandDocsReferencesAsync("compare @docs:react and @docs:vue");

        Assert.Contains("[Documentation for react via Context7]", result);
        Assert.Contains("[Documentation for vue via Context7]", result);
        Assert.DoesNotContain("@docs:react", result);
        Assert.DoesNotContain("@docs:vue", result);
    }

    [Fact]
    public async Task ExpandDocsReferences_SpecialCharsInLibraryName_Matches()
    {
        var preprocessor = CreatePreprocessor();
        var result = await preprocessor.ExpandDocsReferencesAsync("explain @docs:@angular/core");

        Assert.Contains("[Documentation for @angular/core via Context7]", result);
    }

    [Fact]
    public async Task ExpandDocsReferences_DottedLibraryName_Matches()
    {
        var preprocessor = CreatePreprocessor();
        var result = await preprocessor.ExpandDocsReferencesAsync("explain @docs:next.js");

        Assert.Contains("[Documentation for next.js via Context7]", result);
    }

    [Fact]
    public async Task ExpandDocsReferences_NoContext7Available_ReturnsUnchanged()
    {
        var emptyRegistry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var helper = new Context7Helper(emptyRegistry);
        var preprocessor = new DocsPreprocessor(helper);

        var input = "explain @docs:react";
        var result = await preprocessor.ExpandDocsReferencesAsync(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public async Task ExpandDocsReferences_ResolveFails_LeavesTokenAsIs()
    {
        var preprocessor = CreatePreprocessor(resolveResponse: null);
        var input = "explain @docs:unknown-lib";
        var result = await preprocessor.ExpandDocsReferencesAsync(input);

        // Token should remain since fetch failed
        Assert.Contains("@docs:unknown-lib", result);
        Assert.DoesNotContain("[Documentation", result);
    }

    [Fact]
    public async Task ExpandDocsReferences_LargeDocs_Truncated()
    {
        var largeDocs = new string('x', 40_000);
        var preprocessor = CreatePreprocessor(queryResponse: largeDocs);
        var result = await preprocessor.ExpandDocsReferencesAsync("read @docs:react");

        Assert.Contains("[Documentation for react via Context7]", result);
        Assert.Contains("... (truncated)", result);
        // Should be truncated to ~30K + wrapper text
        Assert.True(result.Length < 35_000);
    }

    [Fact]
    public async Task ExpandDocsReferences_MidSentence_ExpandsCorrectly()
    {
        var preprocessor = CreatePreprocessor();
        var result = await preprocessor.ExpandDocsReferencesAsync("how do I use @docs:express middleware?");

        Assert.Contains("[Documentation for express via Context7]", result);
        Assert.Contains("middleware?", result);
    }

    /// <summary>
    /// Minimal fake IOrcaTool for testing.
    /// </summary>
    private sealed class FakeTool : IOrcaTool
    {
        private readonly string? _response;

        public FakeTool(string name, string? response = null)
        {
            Name = name;
            _response = response;
        }

        public string Name { get; }
        public string Description => "Fake tool";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;

        public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
        {
            if (_response is null)
                return Task.FromResult(ToolResult.Error("Not found"));

            return Task.FromResult(ToolResult.Success(_response));
        }
    }
}
