using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Orchestration;
using Xunit;

namespace OpenOrca.Core.Tests.Orchestration;

public class AgentOrchestratorTests
{
    private static AgentOrchestrator CreateOrchestrator(IChatClient? client = null, OrcaConfig? config = null)
    {
        return new AgentOrchestrator(
            client ?? new StubChatClient("Done."),
            config ?? new OrcaConfig(),
            NullLogger<AgentOrchestrator>.Instance);
    }

    [Fact]
    public async Task SpawnAgentAsync_WithType_SetsAgentType()
    {
        var orchestrator = CreateOrchestrator();

        var context = await orchestrator.SpawnAgentAsync("test task", "explore", CancellationToken.None);

        Assert.Equal("explore", context.AgentType);
    }

    [Fact]
    public async Task SpawnAgentAsync_DefaultType_IsGeneral()
    {
        var orchestrator = CreateOrchestrator();

        var context = await orchestrator.SpawnAgentAsync("test task", CancellationToken.None);

        Assert.Equal("general", context.AgentType);
    }

    [Fact]
    public async Task SpawnAgentAsync_UnknownType_FallsBackToGeneral()
    {
        var orchestrator = CreateOrchestrator();

        var context = await orchestrator.SpawnAgentAsync("test task", "nonexistent", CancellationToken.None);

        Assert.Equal("general", context.AgentType);
    }

    [Fact]
    public void FilterToolsForType_Unrestricted_ReturnsAllTools()
    {
        var orchestrator = CreateOrchestrator();
        orchestrator.Tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "a", "read_file"),
            AIFunctionFactory.Create(() => "b", "bash"),
            AIFunctionFactory.Create(() => "c", "write_file")
        };

        var general = AgentTypeRegistry.GetDefault();
        var filtered = orchestrator.FilterToolsForType(general);

        Assert.Equal(3, filtered.Count);
    }

    [Fact]
    public void FilterToolsForType_Restricted_FiltersCorrectly()
    {
        var orchestrator = CreateOrchestrator();
        orchestrator.Tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "a", "read_file"),
            AIFunctionFactory.Create(() => "b", "bash"),
            AIFunctionFactory.Create(() => "c", "glob"),
            AIFunctionFactory.Create(() => "d", "write_file")
        };

        var explore = AgentTypeRegistry.Resolve("explore")!;
        var filtered = orchestrator.FilterToolsForType(explore);

        Assert.Equal(2, filtered.Count); // read_file and glob
        Assert.All(filtered, t =>
        {
            var fn = Assert.IsAssignableFrom<AIFunction>(t);
            Assert.True(fn.Name == "read_file" || fn.Name == "glob");
        });
    }

    [Fact]
    public void FilterToolsForType_NullTools_ReturnsEmpty()
    {
        var orchestrator = CreateOrchestrator();
        orchestrator.Tools = null;

        var explore = AgentTypeRegistry.Resolve("explore")!;
        var filtered = orchestrator.FilterToolsForType(explore);

        Assert.Empty(filtered);
    }

    [Fact]
    public void IsToolAllowed_UnrestrictedType_AllowsAnything()
    {
        var general = AgentTypeRegistry.GetDefault();

        Assert.True(AgentOrchestrator.IsToolAllowed(general, "bash"));
        Assert.True(AgentOrchestrator.IsToolAllowed(general, "write_file"));
        Assert.True(AgentOrchestrator.IsToolAllowed(general, "anything"));
    }

    [Fact]
    public void IsToolAllowed_RestrictedType_AllowsOnlyListed()
    {
        var explore = AgentTypeRegistry.Resolve("explore")!;

        Assert.True(AgentOrchestrator.IsToolAllowed(explore, "read_file"));
        Assert.True(AgentOrchestrator.IsToolAllowed(explore, "glob"));
        Assert.False(AgentOrchestrator.IsToolAllowed(explore, "bash"));
        Assert.False(AgentOrchestrator.IsToolAllowed(explore, "write_file"));
    }

    [Fact]
    public void IsToolAllowed_IsCaseInsensitive()
    {
        var explore = AgentTypeRegistry.Resolve("explore")!;

        Assert.True(AgentOrchestrator.IsToolAllowed(explore, "READ_FILE"));
        Assert.True(AgentOrchestrator.IsToolAllowed(explore, "Glob"));
    }

    /// <summary>
    /// Minimal stub chat client that returns a single text response with no tool calls.
    /// </summary>
    private sealed class StubChatClient : IChatClient
    {
        private readonly string _response;

        public StubChatClient(string response) => _response = response;

        public void Dispose() { }

        public ChatClientMetadata Metadata => new();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var msg = new ChatMessage(ChatRole.Assistant, _response);
            return Task.FromResult(new ChatResponse([msg]));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
