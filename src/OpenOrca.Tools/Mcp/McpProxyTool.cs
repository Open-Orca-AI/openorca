using System.Text.Json;
using OpenOrca.Core.Mcp;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Mcp;

/// <summary>
/// Wraps an MCP tool as an IOrcaTool, proxying execution to the MCP server.
/// Name is prefixed with "mcp_" to avoid collisions with built-in tools.
/// </summary>
public sealed class McpProxyTool : IOrcaTool
{
    private readonly McpClient _client;
    private readonly McpToolDefinition _definition;

    public McpProxyTool(McpClient client, McpToolDefinition definition)
    {
        _client = client;
        _definition = definition;
    }

    public string Name => $"mcp_{_definition.Name}";
    public string Description => $"[MCP:{_client.ServerName}] {_definition.Description}";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Moderate;
    public JsonElement ParameterSchema => _definition.InputSchema;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        try
        {
            var result = await _client.CallToolAsync(_definition.Name, args, ct);
            return ToolResult.Success(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"MCP tool error: {ex.Message}");
        }
    }
}
