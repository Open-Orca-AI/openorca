using OpenOrca.Core.Configuration;
using OpenOrca.Core.Permissions;
using Xunit;

namespace OpenOrca.Core.Tests;

public class PermissionManagerTests
{
    [Fact]
    public async Task ReadOnly_AutoApproved_WhenConfigured()
    {
        var config = new OrcaConfig { Permissions = { AutoApproveReadOnly = true } };
        var manager = new PermissionManager(config);

        var result = await manager.CheckPermissionAsync("read_file", "ReadOnly");
        Assert.True(result);
    }

    [Fact]
    public async Task Moderate_Denied_WhenNoPromptCallback()
    {
        var config = new OrcaConfig { Permissions = { AutoApproveModerate = false } };
        var manager = new PermissionManager(config);

        var result = await manager.CheckPermissionAsync("write_file", "Moderate");
        Assert.False(result);
    }

    [Fact]
    public async Task AutoApproveAll_ApprovesEverything()
    {
        var config = new OrcaConfig { Permissions = { AutoApproveAll = true } };
        var manager = new PermissionManager(config);

        var result = await manager.CheckPermissionAsync("bash", "Dangerous");
        Assert.True(result);
    }

    [Fact]
    public async Task DisabledTools_AlwaysDenied()
    {
        var config = new OrcaConfig { Permissions = { DisabledTools = ["bash"] } };
        var manager = new PermissionManager(config);

        var result = await manager.CheckPermissionAsync("bash", "Dangerous");
        Assert.False(result);
    }

    [Fact]
    public async Task AlwaysApprove_BypassesPrompt()
    {
        var config = new OrcaConfig { Permissions = { AlwaysApprove = ["bash"] } };
        var manager = new PermissionManager(config);

        var result = await manager.CheckPermissionAsync("bash", "Dangerous");
        Assert.True(result);
    }

    [Fact]
    public async Task ApproveAll_StoresSessionApproval()
    {
        var config = new OrcaConfig();
        var manager = new PermissionManager(config);

        manager.PromptForApproval = (_, _) =>
            Task.FromResult(PermissionDecision.ApproveAll);

        // First call triggers the prompt
        var result1 = await manager.CheckPermissionAsync("write_file", "Moderate");
        Assert.True(result1);

        // Remove prompt — should still be approved via session cache
        manager.PromptForApproval = null;
        var result2 = await manager.CheckPermissionAsync("write_file", "Moderate");
        Assert.True(result2);
    }

    [Fact]
    public async Task ResetSessionApprovals_ClearsCache()
    {
        var config = new OrcaConfig();
        var manager = new PermissionManager(config);

        manager.PromptForApproval = (_, _) =>
            Task.FromResult(PermissionDecision.ApproveAll);

        // Approve then reset
        await manager.CheckPermissionAsync("bash", "Dangerous");
        manager.ResetSessionApprovals();

        // Now without prompt, should be denied
        manager.PromptForApproval = null;
        var result = await manager.CheckPermissionAsync("bash", "Dangerous");
        Assert.False(result);
    }
}
