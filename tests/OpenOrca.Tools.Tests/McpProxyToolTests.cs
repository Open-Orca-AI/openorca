using System.Text.Json;
using OpenOrca.Core.Mcp;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Mcp;
using Xunit;

namespace OpenOrca.Tools.Tests;

public class McpProxyToolTests
{
    [Fact]
    public void Name_PrefixedWithMcp()
    {
        var client = new McpClient();
        var def = new McpToolDefinition { Name = "read_file", Description = "Reads a file" };
        var tool = new McpProxyTool(client, def);

        Assert.Equal("mcp_read_file", tool.Name);
    }

    [Fact]
    public void Description_PrefixedWithMcpTag()
    {
        var client = new McpClient();
        var def = new McpToolDefinition { Name = "test_tool", Description = "Does something" };
        var tool = new McpProxyTool(client, def);

        Assert.StartsWith("[MCP:", tool.Description);
        Assert.Contains("Does something", tool.Description);
    }

    [Fact]
    public void RiskLevel_IsModerate()
    {
        var client = new McpClient();
        var def = new McpToolDefinition { Name = "tool", Description = "desc" };
        var tool = new McpProxyTool(client, def);

        Assert.Equal(ToolRiskLevel.Moderate, tool.RiskLevel);
    }

    [Fact]
    public void ParameterSchema_MatchesDefinitionInputSchema()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""").RootElement;
        var client = new McpClient();
        var def = new McpToolDefinition { Name = "tool", Description = "desc", InputSchema = schema.Clone() };
        var tool = new McpProxyTool(client, def);

        Assert.Equal("object", tool.ParameterSchema.GetProperty("type").GetString());
    }

    [Fact]
    public void ImplementsIOrcaTool()
    {
        var client = new McpClient();
        var def = new McpToolDefinition { Name = "tool", Description = "desc" };
        var tool = new McpProxyTool(client, def);

        Assert.IsAssignableFrom<IOrcaTool>(tool);
    }

    [Fact]
    public async Task ExecuteAsync_WhenClientNotConnected_ReturnsError()
    {
        var client = new McpClient();
        var def = new McpToolDefinition { Name = "test", Description = "test" };
        var tool = new McpProxyTool(client, def);

        var args = JsonDocument.Parse("{}").RootElement;
        var result = await tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        Assert.Contains("MCP tool error", result.Content);
    }
}
