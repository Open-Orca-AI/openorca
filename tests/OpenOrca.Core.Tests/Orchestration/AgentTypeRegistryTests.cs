using OpenOrca.Core.Orchestration;
using Xunit;

namespace OpenOrca.Core.Tests.Orchestration;

public class AgentTypeRegistryTests
{
    [Fact]
    public void Resolve_KnownType_ReturnsDefinition()
    {
        var result = AgentTypeRegistry.Resolve("explore");

        Assert.NotNull(result);
        Assert.Equal("explore", result.Name);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var result = AgentTypeRegistry.Resolve("EXPLORE");

        Assert.NotNull(result);
        Assert.Equal("explore", result.Name);
    }

    [Fact]
    public void Resolve_UnknownType_ReturnsNull()
    {
        var result = AgentTypeRegistry.Resolve("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetDefault_ReturnsGeneral()
    {
        var result = AgentTypeRegistry.GetDefault();

        Assert.Equal("general", result.Name);
        Assert.Null(result.AllowedTools);
    }

    [Fact]
    public void GetAll_ReturnsFiveTypes()
    {
        var all = AgentTypeRegistry.GetAll();

        Assert.Equal(5, all.Count);
    }

    [Theory]
    [InlineData("explore")]
    [InlineData("plan")]
    [InlineData("bash")]
    [InlineData("review")]
    [InlineData("general")]
    public void Resolve_AllBuiltInTypes_Exist(string typeName)
    {
        var result = AgentTypeRegistry.Resolve(typeName);

        Assert.NotNull(result);
        Assert.Equal(typeName, result.Name);
        Assert.NotEmpty(result.Description);
        Assert.NotEmpty(result.PromptResourceName);
    }

    [Fact]
    public void ExploreType_HasReadOnlyTools()
    {
        var explore = AgentTypeRegistry.Resolve("explore")!;

        Assert.NotNull(explore.AllowedTools);
        Assert.Contains("read_file", explore.AllowedTools);
        Assert.Contains("glob", explore.AllowedTools);
        Assert.Contains("grep", explore.AllowedTools);
        Assert.DoesNotContain("bash", explore.AllowedTools);
        Assert.DoesNotContain("write_file", explore.AllowedTools);
    }

    [Fact]
    public void BashType_HasExecutionTools()
    {
        var bash = AgentTypeRegistry.Resolve("bash")!;

        Assert.NotNull(bash.AllowedTools);
        Assert.Contains("bash", bash.AllowedTools);
        Assert.DoesNotContain("write_file", bash.AllowedTools);
        Assert.DoesNotContain("glob", bash.AllowedTools);
    }

    [Fact]
    public void GeneralType_HasUnrestrictedTools()
    {
        var general = AgentTypeRegistry.Resolve("general")!;

        Assert.Null(general.AllowedTools);
    }

    [Fact]
    public void ReviewType_HasGitAndReadTools()
    {
        var review = AgentTypeRegistry.Resolve("review")!;

        Assert.NotNull(review.AllowedTools);
        Assert.Contains("git_diff", review.AllowedTools);
        Assert.Contains("git_log", review.AllowedTools);
        Assert.Contains("read_file", review.AllowedTools);
        Assert.DoesNotContain("bash", review.AllowedTools);
    }

    [Fact]
    public void PlanType_HasResearchTools()
    {
        var plan = AgentTypeRegistry.Resolve("plan")!;

        Assert.NotNull(plan.AllowedTools);
        Assert.Contains("web_search", plan.AllowedTools);
        Assert.Contains("web_fetch", plan.AllowedTools);
        Assert.Contains("read_file", plan.AllowedTools);
        Assert.DoesNotContain("bash", plan.AllowedTools);
    }
}
