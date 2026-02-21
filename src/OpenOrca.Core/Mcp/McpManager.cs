using Microsoft.Extensions.Logging;
using OpenOrca.Core.Configuration;

namespace OpenOrca.Core.Mcp;

/// <summary>
/// Manages lifecycle of all MCP server connections.
/// Returns (client, tool definition) pairs that callers can wrap as proxy tools.
/// </summary>
public sealed class McpManager : IAsyncDisposable
{
    private readonly List<McpClient> _clients = [];
    private readonly ILogger? _logger;

    public IReadOnlyList<McpClient> Clients => _clients;

    public McpManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initialize all configured MCP servers and return their tools as (client, definition) pairs.
    /// </summary>
    public async Task<List<(McpClient Client, McpToolDefinition Tool)>> InitializeAsync(
        Dictionary<string, McpServerConfig> servers, CancellationToken ct)
    {
        var allTools = new List<(McpClient, McpToolDefinition)>();

        foreach (var (name, config) in servers)
        {
            if (!config.Enabled)
            {
                _logger?.LogInformation("MCP server '{Name}' is disabled, skipping", name);
                continue;
            }

            try
            {
                var client = new McpClient(_logger);
                await client.ConnectAsync(name, config, ct);
                _clients.Add(client);

                var tools = await client.ListToolsAsync(ct);
                foreach (var tool in tools)
                {
                    allTools.Add((client, tool));
                }

                _logger?.LogInformation("MCP server '{Name}' connected: {Count} tools", name, tools.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to connect to MCP server '{Name}'", name);
            }
        }

        return allTools;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
        _clients.Clear();
    }
}
