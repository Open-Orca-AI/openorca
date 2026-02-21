using OpenOrca.Core.Configuration;
using OpenOrca.Core.Permissions;
using Xunit;

namespace OpenOrca.Core.Tests;

public class PermissionGlobTests
{
    [Fact]
    public async Task DenyPattern_BlocksMatchingToolCall()
    {
        var config = new OrcaConfig
        {
            Permissions =
            {
                AutoApproveAll = true,
                DenyPatterns = ["Bash(rm -rf *)"]
            }
        };
        var manager = new PermissionManager(config);

        var result = await manager.CheckPermissionAsync("bash", "Dangerous", """{"command": "rm -rf /tmp/test"}""");
        Assert.False(result);
    }

    [Fact]
    public async Task AllowPattern_ApprovesMatchingToolCall()
    {
        var config = new OrcaConfig
        {
            Permissions =
            {
                AutoApproveReadOnly = false,
                AutoApproveModerate = false,
                AllowPatterns = ["Bash(git *)"]
            }
        };
        var manager = new PermissionManager(config);

        var result = await manager.CheckPermissionAsync("bash", "Dangerous", """{"command": "git status"}""");
        Assert.True(result);
    }

    [Fact]
    public async Task DenyPattern_WinsOverAllowPattern()
    {
        var config = new OrcaConfig
        {
            Permissions =
            {
                AllowPatterns = ["Bash(sudo *)"],
                DenyPatterns = ["Bash(sudo *)"]
            }
        };
        var manager = new PermissionManager(config);

        var result = await manager.CheckPermissionAsync("bash", "Dangerous", """{"command": "sudo rm -rf /"}""");
        Assert.False(result);
    }

    [Fact]
    public async Task NoMatchingPattern_FallsBackToOriginalBehavior()
    {
        var config = new OrcaConfig
        {
            Permissions =
            {
                AutoApproveAll = true,
                DenyPatterns = ["Bash(rm *)"]
            }
        };
        var manager = new PermissionManager(config);

        // Not matching the deny pattern — falls back to auto-approve
        var result = await manager.CheckPermissionAsync("bash", "Dangerous", """{"command": "git status"}""");
        Assert.True(result);
    }

    [Fact]
    public async Task NullArgs_FallsBackToOriginalBehavior()
    {
        var config = new OrcaConfig
        {
            Permissions =
            {
                AutoApproveAll = true,
                DenyPatterns = ["Bash(rm *)"]
            }
        };
        var manager = new PermissionManager(config);

        // No args to match against — deny pattern can't match, falls through
        var result = await manager.CheckPermissionAsync("bash", "Dangerous");
        Assert.True(result);
    }

    [Fact]
    public async Task FileToolPatterns_MatchPath()
    {
        var config = new OrcaConfig
        {
            Permissions =
            {
                AllowPatterns = ["write_file(src/**)"],
                DenyPatterns = ["write_file(*.exe)"]
            }
        };
        var manager = new PermissionManager(config);

        var srcResult = await manager.CheckPermissionAsync("write_file", "Moderate",
            """{"path": "src/main.cs", "content": "test"}""");
        Assert.True(srcResult);

        var exeResult = await manager.CheckPermissionAsync("write_file", "Moderate",
            """{"path": "virus.exe", "content": "test"}""");
        Assert.False(exeResult);
    }
}
