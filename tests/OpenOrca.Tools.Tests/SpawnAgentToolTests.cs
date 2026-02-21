using System.Text.Json;
using OpenOrca.Tools.Agent;
using Xunit;

namespace OpenOrca.Tools.Tests;

public class SpawnAgentToolTests
{
    private static JsonElement MakeArgs(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Name_IsSpawnAgent()
    {
        var tool = new SpawnAgentTool();
        Assert.Equal("spawn_agent", tool.Name);
    }

    [Fact]
    public void Description_ListsAgentTypes()
    {
        var tool = new SpawnAgentTool();
        Assert.Contains("explore", tool.Description);
        Assert.Contains("plan", tool.Description);
        Assert.Contains("bash", tool.Description);
        Assert.Contains("review", tool.Description);
        Assert.Contains("general", tool.Description);
    }

    [Fact]
    public async Task ExecuteAsync_PassesTaskAndAgentType()
    {
        string? capturedTask = null;
        string? capturedType = null;

        var tool = new SpawnAgentTool
        {
            AgentSpawner = (task, agentType, ct) =>
            {
                capturedTask = task;
                capturedType = agentType;
                return Task.FromResult("result");
            }
        };

        var args = MakeArgs("""{"task": "find files", "agent_type": "explore"}""");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("find files", capturedTask);
        Assert.Equal("explore", capturedType);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsToGeneral_WhenNoAgentType()
    {
        string? capturedType = null;

        var tool = new SpawnAgentTool
        {
            AgentSpawner = (task, agentType, ct) =>
            {
                capturedType = agentType;
                return Task.FromResult("result");
            }
        };

        var args = MakeArgs("""{"task": "do something"}""");
        await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.Equal("general", capturedType);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_WhenSpawnerNotConfigured()
    {
        var tool = new SpawnAgentTool();
        var args = MakeArgs("""{"task": "test"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not configured", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_OnSpawnerException()
    {
        var tool = new SpawnAgentTool
        {
            AgentSpawner = (_, _, _) => throw new InvalidOperationException("boom")
        };

        var args = MakeArgs("""{"task": "test"}""");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("boom", result.Content);
    }

    [Fact]
    public void ParameterSchema_IncludesAgentTypeEnum()
    {
        var tool = new SpawnAgentTool();
        var schema = tool.ParameterSchema;

        var agentTypeProp = schema.GetProperty("properties").GetProperty("agent_type");
        var enumValues = agentTypeProp.GetProperty("enum");

        var values = new List<string>();
        foreach (var val in enumValues.EnumerateArray())
            values.Add(val.GetString()!);

        Assert.Contains("explore", values);
        Assert.Contains("plan", values);
        Assert.Contains("bash", values);
        Assert.Contains("review", values);
        Assert.Contains("general", values);
    }

    [Fact]
    public void ParameterSchema_TaskIsRequired()
    {
        var tool = new SpawnAgentTool();
        var schema = tool.ParameterSchema;

        var required = schema.GetProperty("required");
        var requiredNames = new List<string>();
        foreach (var val in required.EnumerateArray())
            requiredNames.Add(val.GetString()!);

        Assert.Contains("task", requiredNames);
        Assert.DoesNotContain("agent_type", requiredNames);
    }
}
