using OpenOrca.Core.Orchestration;
using Xunit;

namespace OpenOrca.Core.Tests.Orchestration;

[Collection("AgentRegistry")]
public class CustomAgentLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalCwd;

    public CustomAgentLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openorca-agent-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _originalCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir);

        // Clean up any custom registrations from other tests
        AgentTypeRegistry.ClearCustom();
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        AgentTypeRegistry.ClearCustom();
    }

    [Fact]
    public void ParseAgentContent_ValidFrontmatter_ReturnsDefinition()
    {
        var content = """
            ---
            name: security
            description: Security analysis specialist
            tools: [read_file, grep, think]
            ---

            You are a security analysis specialist.
            ## Task
            {{TASK}}
            """;

        var loader = new CustomAgentLoader();
        var def = loader.ParseAgentContent(content, "/fake/security.md");

        Assert.NotNull(def);
        Assert.Equal("security", def.Name);
        Assert.Equal("Security analysis specialist", def.Description);
        Assert.Equal(["read_file", "grep", "think"], def.AllowedTools);
    }

    [Fact]
    public void ParseAgentContent_MissingName_ReturnsNull()
    {
        var content = """
            ---
            description: No name agent
            tools: [read_file]
            ---

            Body text.
            """;

        var loader = new CustomAgentLoader();
        var def = loader.ParseAgentContent(content, "/fake/noname.md");

        Assert.Null(def);
    }

    [Fact]
    public void ParseAgentContent_MissingTools_ReturnsNull()
    {
        var content = """
            ---
            name: bad
            description: No tools
            ---

            Body text.
            """;

        var loader = new CustomAgentLoader();
        var def = loader.ParseAgentContent(content, "/fake/bad.md");

        Assert.Null(def);
    }

    [Fact]
    public void ParseAgentContent_NoFrontmatter_ReturnsNull()
    {
        var content = "Just plain markdown, no frontmatter.";

        var loader = new CustomAgentLoader();
        var def = loader.ParseAgentContent(content, "/fake/plain.md");

        Assert.Null(def);
    }

    [Fact]
    public void ParseAgentContent_ValidatesToolNames_FiltersInvalid()
    {
        var content = """
            ---
            name: restricted
            description: Only valid tools
            tools: [read_file, nonexistent_tool, grep]
            ---

            Body.
            """;

        var validTools = new List<string> { "read_file", "grep", "bash" };
        var loader = new CustomAgentLoader();
        var def = loader.ParseAgentContent(content, "/fake/restricted.md", validTools);

        Assert.NotNull(def);
        Assert.Equal(["read_file", "grep"], def.AllowedTools);
    }

    [Fact]
    public void ParseAgentContent_AllToolsInvalid_ReturnsNull()
    {
        var content = """
            ---
            name: empty
            description: No valid tools
            tools: [fake1, fake2]
            ---

            Body.
            """;

        var validTools = new List<string> { "read_file", "grep" };
        var loader = new CustomAgentLoader();
        var def = loader.ParseAgentContent(content, "/fake/empty.md", validTools);

        Assert.Null(def);
    }

    [Fact]
    public void SubstituteTemplateVars_ReplacesAllVars()
    {
        var template = "Task: {{TASK}}\nCWD: {{CWD}}\nPlatform: {{PLATFORM}}";

        var result = CustomAgentLoader.SubstituteTemplateVars(template, "do stuff", "/home/user", "linux");

        Assert.Equal("Task: do stuff\nCWD: /home/user\nPlatform: linux", result);
    }

    [Fact]
    public void DiscoverAgents_ProjectDir_FindsAgents()
    {
        // Create a fake project root with .orca/agents/
        var orcaDir = Path.Combine(_tempDir, ".orca", "agents");
        Directory.CreateDirectory(orcaDir);

        // Create a .git dir so FindProjectRoot detects it
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));

        File.WriteAllText(Path.Combine(orcaDir, "test-agent.md"), """
            ---
            name: test-agent
            description: Test agent
            tools: [read_file, think]
            ---

            You are a test agent.
            {{TASK}}
            """);

        var loader = new CustomAgentLoader();
        var agents = loader.DiscoverAgents();

        Assert.Contains("test-agent", agents.Keys);
        Assert.Equal("Test agent", agents["test-agent"].Description);
    }

    [Fact]
    public void RegisterCustom_CustomOverridesBuiltIn_OnResolve()
    {
        // Use a unique name to avoid interfering with parallel tests that resolve built-in types
        var customGeneral = new AgentTypeDefinition(
            Name: "custom-override-test",
            Description: "Custom override test agent",
            AllowedTools: ["read_file"],
            PromptResourceName: "/fake/override.md");

        AgentTypeRegistry.RegisterCustom(new Dictionary<string, AgentTypeDefinition>
        {
            ["custom-override-test"] = customGeneral
        });

        var resolved = AgentTypeRegistry.Resolve("custom-override-test");

        Assert.NotNull(resolved);
        Assert.Equal("Custom override test agent", resolved.Description);

        // Verify custom types take precedence in GetAll
        var all = AgentTypeRegistry.GetAll();
        Assert.Contains(all, a => a.Name == "custom-override-test");
    }

    [Fact]
    public void GetAll_IncludesBothBuiltInAndCustom()
    {
        var custom = new AgentTypeDefinition(
            Name: "security",
            Description: "Custom security agent",
            AllowedTools: ["read_file"],
            PromptResourceName: "/fake/security.md");

        AgentTypeRegistry.Register(custom);

        var all = AgentTypeRegistry.GetAll();

        Assert.Contains(all, a => a.Name == "security");
        Assert.Contains(all, a => a.Name == "explore");
        Assert.Contains(all, a => a.Name == "general");
    }

    [Fact]
    public void GetAllNames_IncludesCustomNames()
    {
        var custom = new AgentTypeDefinition(
            Name: "perf",
            Description: "Performance agent",
            AllowedTools: ["read_file"],
            PromptResourceName: "/fake/perf.md");

        AgentTypeRegistry.Register(custom);

        var names = AgentTypeRegistry.GetAllNames();

        Assert.Contains("perf", names);
        Assert.Contains("explore", names);
    }

    [Fact]
    public void ParseAgentContent_QuotedTools_ParsedCorrectly()
    {
        var content = """
            ---
            name: quoted
            description: Agent with quoted tools
            tools: ["read_file", "grep", "bash"]
            ---

            Body.
            """;

        var loader = new CustomAgentLoader();
        var def = loader.ParseAgentContent(content, "/fake/quoted.md");

        Assert.NotNull(def);
        Assert.Equal(["read_file", "grep", "bash"], def.AllowedTools);
    }
}
