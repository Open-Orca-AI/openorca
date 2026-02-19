using OpenOrca.Core.Configuration;
using Xunit;

namespace OpenOrca.Core.Tests;

public class ConfigManagerTests
{
    [Fact]
    public void DefaultConfig_HasSensibleDefaults()
    {
        var config = new OrcaConfig();

        Assert.Equal("http://localhost:1234/v1", config.LmStudio.BaseUrl);
        Assert.Equal("lm-studio", config.LmStudio.ApiKey);
        Assert.Null(config.LmStudio.Model);
        Assert.Equal(0.7f, config.LmStudio.Temperature);
        Assert.Equal(120, config.LmStudio.TimeoutSeconds);
        Assert.True(config.Permissions.AutoApproveReadOnly);
        Assert.False(config.Permissions.AutoApproveAll);
        Assert.True(config.Session.AutoSave);
        Assert.Equal(100, config.Session.MaxSessions);
    }

    [Fact]
    public async Task LoadAsync_PopulatesConfig()
    {
        var manager = new ConfigManager();
        await manager.LoadAsync();

        Assert.NotNull(manager.Config);
        Assert.NotNull(manager.Config.LmStudio);
        Assert.NotNull(manager.Config.LmStudio.BaseUrl);
        Assert.NotNull(manager.Config.Permissions);
        Assert.NotNull(manager.Config.Session);
        Assert.NotNull(manager.Config.Context);
        Assert.NotNull(manager.Config.Hooks);
    }

    // ── ContextConfig defaults ──

    [Fact]
    public void ContextConfig_HasSensibleDefaults()
    {
        var config = new OrcaConfig();

        Assert.Equal(8192, config.Context.ContextWindowSize);
        Assert.Equal(0.8f, config.Context.AutoCompactThreshold);
        Assert.Equal(4, config.Context.CompactPreserveLastN);
        Assert.True(config.Context.AutoCompactEnabled);
    }

    [Fact]
    public void ContextConfig_IsNotNull_ByDefault()
    {
        var config = new OrcaConfig();
        Assert.NotNull(config.Context);
    }

    // ── HooksConfig defaults ──

    [Fact]
    public void HooksConfig_HasEmptyDictionaries_ByDefault()
    {
        var config = new OrcaConfig();

        Assert.NotNull(config.Hooks);
        Assert.Empty(config.Hooks.PreToolHooks);
        Assert.Empty(config.Hooks.PostToolHooks);
    }

    [Fact]
    public void HooksConfig_CanAddHooks()
    {
        var config = new OrcaConfig();
        config.Hooks.PreToolHooks["bash"] = "echo pre-bash";
        config.Hooks.PostToolHooks["*"] = "echo post-any";

        Assert.Single(config.Hooks.PreToolHooks);
        Assert.Single(config.Hooks.PostToolHooks);
        Assert.Equal("echo pre-bash", config.Hooks.PreToolHooks["bash"]);
        Assert.Equal("echo post-any", config.Hooks.PostToolHooks["*"]);
    }
}
