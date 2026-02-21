using OpenOrca.Core.Orchestration;
using Xunit;

namespace OpenOrca.Core.Tests.Orchestration;

[Collection("AgentRegistry")]
public class AgentPromptLoaderTests : IDisposable
{
    private readonly AgentPromptLoader _loader = new();

    public AgentPromptLoaderTests()
    {
        // Ensure no custom types interfere with built-in type resolution
        AgentTypeRegistry.ClearCustom();
    }

    public void Dispose()
    {
        AgentTypeRegistry.ClearCustom();
    }

    [Theory]
    [InlineData("explore")]
    [InlineData("plan")]
    [InlineData("bash")]
    [InlineData("review")]
    [InlineData("general")]
    public void LoadPrompt_AllBuiltInTypes_ReturnNonNull(string typeName)
    {
        var typeDef = AgentTypeRegistry.Resolve(typeName)!;

        var prompt = _loader.LoadPrompt(typeDef, "test task", "/tmp", "Linux");

        Assert.NotNull(prompt);
        Assert.NotEmpty(prompt);
    }

    [Fact]
    public void LoadPrompt_SubstitutesTaskVariable()
    {
        var typeDef = AgentTypeRegistry.Resolve("explore")!;

        var prompt = _loader.LoadPrompt(typeDef, "find all controllers", "/tmp", "Linux");

        Assert.Contains("find all controllers", prompt);
        Assert.DoesNotContain("{{TASK}}", prompt);
    }

    [Fact]
    public void LoadPrompt_SubstitutesCwdVariable()
    {
        var typeDef = AgentTypeRegistry.Resolve("explore")!;

        var prompt = _loader.LoadPrompt(typeDef, "test", "/home/user/project", "Linux");

        Assert.Contains("/home/user/project", prompt);
        Assert.DoesNotContain("{{CWD}}", prompt);
    }

    [Fact]
    public void LoadPrompt_SubstitutesPlatformVariable()
    {
        var typeDef = AgentTypeRegistry.Resolve("explore")!;

        var prompt = _loader.LoadPrompt(typeDef, "test", "/tmp", "Windows 11");

        Assert.Contains("Windows 11", prompt);
        Assert.DoesNotContain("{{PLATFORM}}", prompt);
    }

    [Fact]
    public void LoadPrompt_MissingResource_ReturnsNull()
    {
        var fakeDef = new AgentTypeDefinition(
            Name: "fake",
            Description: "does not exist",
            AllowedTools: null,
            PromptResourceName: "nonexistent.md");

        var prompt = _loader.LoadPrompt(fakeDef, "test", "/tmp", "Linux");

        Assert.Null(prompt);
    }

    [Fact]
    public void LoadPrompt_ExplorePrompt_ContainsReadOnlyConstraints()
    {
        var typeDef = AgentTypeRegistry.Resolve("explore")!;

        var prompt = _loader.LoadPrompt(typeDef, "test task", "/tmp", "Linux");

        Assert.Contains("read_file", prompt);
        Assert.Contains("glob", prompt);
        Assert.Contains("grep", prompt);
    }

    [Fact]
    public void LoadPrompt_BashPrompt_ContainsBashTool()
    {
        var typeDef = AgentTypeRegistry.Resolve("bash")!;

        var prompt = _loader.LoadPrompt(typeDef, "run tests", "/tmp", "Linux");

        Assert.Contains("bash", prompt);
        Assert.Contains("command execution", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
