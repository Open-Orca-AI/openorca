using OpenOrca.Core.Configuration;
using Xunit;

namespace OpenOrca.Core.Tests;

public class ThinkingBudgetTests
{
    [Fact]
    public void ThinkingConfig_Defaults_ZeroBudgetAndNotVisible()
    {
        var config = new ThinkingConfig();
        Assert.Equal(0, config.BudgetTokens);
        Assert.False(config.DefaultVisible);
    }

    [Fact]
    public void ThinkingConfig_CanSetBudgetTokens()
    {
        var config = new ThinkingConfig { BudgetTokens = 200 };
        Assert.Equal(200, config.BudgetTokens);
    }

    [Fact]
    public void ThinkingConfig_CanSetDefaultVisible()
    {
        var config = new ThinkingConfig { DefaultVisible = true };
        Assert.True(config.DefaultVisible);
    }

    [Fact]
    public void OrcaConfig_HasThinkingConfig()
    {
        var config = new OrcaConfig();
        Assert.NotNull(config.Thinking);
        Assert.Equal(0, config.Thinking.BudgetTokens);
        Assert.False(config.Thinking.DefaultVisible);
    }

    [Fact]
    public void OrcaConfig_SandboxMode_DefaultsFalse()
    {
        var config = new OrcaConfig();
        Assert.False(config.SandboxMode);
    }

    [Fact]
    public void OrcaConfig_AllowedDirectory_DefaultsNull()
    {
        var config = new OrcaConfig();
        Assert.Null(config.AllowedDirectory);
    }

    [Fact]
    public void OrcaConfig_McpServers_DefaultsEmpty()
    {
        var config = new OrcaConfig();
        Assert.NotNull(config.McpServers);
        Assert.Empty(config.McpServers);
    }

    [Fact]
    public void McpServerConfig_Defaults()
    {
        var config = new McpServerConfig();
        Assert.Equal("", config.Command);
        Assert.Empty(config.Args);
        Assert.Empty(config.Env);
        Assert.True(config.Enabled);
    }
}
