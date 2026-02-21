using OpenOrca.Core.Configuration;
using OpenOrca.Core.Mcp;
using Xunit;

namespace OpenOrca.Core.Tests;

public class McpManagerTests
{
    [Fact]
    public async Task InitializeAsync_EmptyConfig_ReturnsNoTools()
    {
        var manager = new McpManager();
        var servers = new Dictionary<string, McpServerConfig>();

        var tools = await manager.InitializeAsync(servers, CancellationToken.None);

        Assert.Empty(tools);
        Assert.Empty(manager.Clients);
    }

    [Fact]
    public async Task InitializeAsync_DisabledServer_Skipped()
    {
        var manager = new McpManager();
        var servers = new Dictionary<string, McpServerConfig>
        {
            ["test"] = new McpServerConfig
            {
                Command = "nonexistent_command_12345",
                Enabled = false
            }
        };

        var tools = await manager.InitializeAsync(servers, CancellationToken.None);

        Assert.Empty(tools);
        Assert.Empty(manager.Clients);
    }

    [Fact]
    public async Task InitializeAsync_InvalidCommand_HandlesGracefully()
    {
        var manager = new McpManager();
        var servers = new Dictionary<string, McpServerConfig>
        {
            ["test"] = new McpServerConfig
            {
                Command = "this_command_does_not_exist_xyz_123",
                Enabled = true
            }
        };

        // Should not throw — failures are logged and skipped
        var tools = await manager.InitializeAsync(servers, CancellationToken.None);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task DisposeAsync_EmptyManager_NoError()
    {
        var manager = new McpManager();
        await manager.DisposeAsync(); // Should not throw
    }

    [Fact]
    public async Task DisposeAsync_AfterInit_ClearsClients()
    {
        var manager = new McpManager();
        await manager.InitializeAsync(new Dictionary<string, McpServerConfig>(), CancellationToken.None);
        await manager.DisposeAsync();

        Assert.Empty(manager.Clients);
    }
}
