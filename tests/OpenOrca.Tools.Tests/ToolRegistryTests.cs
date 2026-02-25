using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Tools.Registry;
using Xunit;

namespace OpenOrca.Tools.Tests;

public class ToolRegistryTests
{
    [Fact]
    public void DiscoverTools_FindsAllTools()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);

        var tools = registry.GetAll();
        Assert.NotEmpty(tools);
    }

    [Fact]
    public void DiscoverTools_FindsExpected39Tools()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);

        var tools = registry.GetAll();
        Assert.Equal(39, tools.Count);
    }

    [Fact]
    public void DiscoverTools_FindsReadFileTool()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);

        var tool = registry.Resolve("read_file");
        Assert.NotNull(tool);
        Assert.Equal("read_file", tool.Name);
    }

    [Fact]
    public void DiscoverTools_BashToolIsEnabled()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);

        var tool = registry.Resolve("bash");
        Assert.NotNull(tool);
        Assert.Equal("bash", tool.Name);
    }

    [Fact]
    public void DiscoverTools_FindsAllNewTools()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);

        var newTools = new[]
        {
            "delete_file", "move_file", "copy_file",
            "git_push", "git_pull", "git_stash",
            "web_fetch", "web_search",
            "think", "task_list",
            "github",
            "network_diagnostics", "archive",
            "multi_edit"
        };

        foreach (var name in newTools)
        {
            var tool = registry.Resolve(name);
            Assert.NotNull(tool);
        }
    }

    [Fact]
    public void Resolve_ReturnsNull_ForUnknownTool()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);

        var tool = registry.Resolve("nonexistent");
        Assert.Null(tool);
    }

    [Fact]
    public void GenerateAITools_ReturnsCorrectCount()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);

        var aiTools = registry.GenerateAITools();
        Assert.Equal(registry.GetAll().Count, aiTools.Count);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.DiscoverTools(typeof(ToolRegistry).Assembly);

        var tool1 = registry.Resolve("read_file");
        var tool2 = registry.Resolve("READ_FILE");
        var tool3 = registry.Resolve("Read_File");

        Assert.NotNull(tool1);
        Assert.Same(tool1, tool2);
        Assert.Same(tool1, tool3);
    }
}
