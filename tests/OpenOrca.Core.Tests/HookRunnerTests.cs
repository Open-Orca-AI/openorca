using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Hooks;
using Xunit;

namespace OpenOrca.Core.Tests;

public class HookRunnerTests
{
    private static HookRunner CreateRunner(HooksConfig? config = null)
    {
        return new HookRunner(
            config ?? new HooksConfig(),
            NullLogger<HookRunner>.Instance);
    }

    [Fact]
    public async Task RunPreHookAsync_ReturnsTrue_WhenNoHookConfigured()
    {
        var runner = CreateRunner();

        var result = await runner.RunPreHookAsync("bash", "{}", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task RunPreHookAsync_RunsSpecificHook()
    {
        // "echo ok" exits with 0 → should not block
        var config = new HooksConfig();
        if (OperatingSystem.IsWindows())
            config.PreToolHooks["bash"] = "echo ok";
        else
            config.PreToolHooks["bash"] = "echo ok";

        var runner = CreateRunner(config);

        var result = await runner.RunPreHookAsync("bash", "{}", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task RunPreHookAsync_BlocksTool_WhenHookExitsNonZero()
    {
        var config = new HooksConfig();
        if (OperatingSystem.IsWindows())
            config.PreToolHooks["bash"] = "exit /b 1";
        else
            config.PreToolHooks["bash"] = "exit 1";

        var runner = CreateRunner(config);

        var result = await runner.RunPreHookAsync("bash", "{}", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task RunPreHookAsync_WildcardHook_MatchesAnyTool()
    {
        var config = new HooksConfig();
        if (OperatingSystem.IsWindows())
            config.PreToolHooks["*"] = "echo wildcard";
        else
            config.PreToolHooks["*"] = "echo wildcard";

        var runner = CreateRunner(config);

        var result = await runner.RunPreHookAsync("any_tool_name", "{}", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task RunPreHookAsync_SpecificHook_TakesPrecedence_OverWildcard()
    {
        var config = new HooksConfig();
        if (OperatingSystem.IsWindows())
        {
            config.PreToolHooks["bash"] = "exit /b 1"; // blocks
            config.PreToolHooks["*"] = "echo ok"; // allows
        }
        else
        {
            config.PreToolHooks["bash"] = "exit 1"; // blocks
            config.PreToolHooks["*"] = "echo ok"; // allows
        }

        var runner = CreateRunner(config);

        // bash should be blocked by its specific hook
        var bashResult = await runner.RunPreHookAsync("bash", "{}", CancellationToken.None);
        Assert.False(bashResult);

        // other_tool should use wildcard and pass
        var otherResult = await runner.RunPreHookAsync("other_tool", "{}", CancellationToken.None);
        Assert.True(otherResult);
    }

    [Fact]
    public async Task RunPostHookAsync_DoesNotThrow_WhenNoHookConfigured()
    {
        var runner = CreateRunner();

        // Should complete without throwing
        await runner.RunPostHookAsync("bash", "{}", "result", false, CancellationToken.None);
    }

    [Fact]
    public async Task RunPostHookAsync_DoesNotThrow_WhenHookFails()
    {
        var config = new HooksConfig();
        if (OperatingSystem.IsWindows())
            config.PostToolHooks["bash"] = "exit /b 1";
        else
            config.PostToolHooks["bash"] = "exit 1";

        var runner = CreateRunner(config);

        // Post-hooks are fire-and-forget — should not throw even if hook fails
        await runner.RunPostHookAsync("bash", "{}", "result", false, CancellationToken.None);
    }
}
