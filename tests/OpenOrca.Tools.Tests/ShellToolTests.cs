using System.Diagnostics;
using OpenOrca.Tools.FileSystem;
using OpenOrca.Tools.Shell;
using Xunit;
using static OpenOrca.Tools.Tests.TestHelpers;

namespace OpenOrca.Tools.Tests;

public class ShellToolTests
{
    // ── BashTool ──

    [Fact]
    public async Task BashTool_SimpleCommand_ReturnsOutput()
    {
        var tool = new BashTool();
        var command = "echo hello";
        var args = MakeArgs($"{{\"command\": \"{command}\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        Assert.Contains("hello", result.Content);
    }

    [Fact]
    public async Task BashTool_FailingCommand_ReturnsOutput()
    {
        var tool = new BashTool();
        var command = OperatingSystem.IsWindows() ? "cmd /c exit 1" : "false";
        var args = MakeArgs($"{{\"command\": \"{command}\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.Contains("exit code", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BashTool_WithWorkingDirectory_RunsInDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bash_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var tool = new BashTool();
            var command = OperatingSystem.IsWindows() ? "cd" : "pwd";
            var escapedPath = tempDir.Replace("\\", "\\\\");
            var args = MakeArgs($"{{\"command\": \"{command}\", \"working_directory\": \"{escapedPath}\"}}");
            var result = await tool.ExecuteAsync(args, CancellationToken.None);

            Assert.True(!result.IsError);
            Assert.Contains(Path.GetFileName(tempDir), result.Content);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task BashTool_Timeout_RespectsTimeout()
    {
        var tool = new BashTool();
        var command = OperatingSystem.IsWindows() ? "ping -n 10 127.0.0.1" : "sleep 10";
        var args = MakeArgs($"{{\"command\": \"{command}\", \"timeout_seconds\": 1}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.Contains("timed out", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BashTool_MultilineOutput_ReturnsFull()
    {
        var tool = new BashTool();
        var command = OperatingSystem.IsWindows()
            ? "echo line1 && echo line2 && echo line3"
            : "echo line1; echo line2; echo line3";
        var args = MakeArgs($"{{\"command\": \"{command}\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        Assert.Contains("line1", result.Content);
        Assert.Contains("line2", result.Content);
        Assert.Contains("line3", result.Content);
    }

    // ── BashTool Idle Timeout ──

    [Fact]
    public async Task BashTool_IdleTimeout_FiresWhenNoStdout()
    {
        var tool = new BashTool { IdleTimeoutSeconds = 2 };
        var command = OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1 > nul" : "sleep 30";
        var args = MakeArgs($"{{\"command\": \"{command}\"}}");

        var sw = Stopwatch.StartNew();
        var result = await tool.ExecuteAsync(args, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsError);
        Assert.Contains("idle timeout", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("start_background_process", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed.TotalSeconds < 15, $"Should abort quickly, took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task BashTool_IdleTimeout_ResetsOnActivity()
    {
        var tool = new BashTool { IdleTimeoutSeconds = 3 };
        // Echo, sleep (less than idle timeout), echo again — should succeed
        var command = OperatingSystem.IsWindows()
            ? "echo first && ping -n 3 127.0.0.1 > nul && echo second"
            : "echo first; sleep 2; echo second";
        var args = MakeArgs($"{{\"command\": \"{command}\"}}");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError, $"Expected success but got: {result.Content}");
        Assert.Contains("first", result.Content);
        Assert.Contains("second", result.Content);
    }

    [Fact]
    public async Task BashTool_IdleTimeout_DisabledWhenZero()
    {
        var tool = new BashTool { IdleTimeoutSeconds = 0 };
        // Sleep 3s then echo — with idle disabled, should succeed
        var command = OperatingSystem.IsWindows()
            ? "ping -n 4 127.0.0.1 > nul && echo done"
            : "sleep 3; echo done";
        var args = MakeArgs($"{{\"command\": \"{command}\", \"timeout_seconds\": 10}}");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError, $"Expected success but got: {result.Content}");
        Assert.Contains("done", result.Content);
    }

    [Fact]
    public async Task BashTool_IdleTimeout_PerCallOverride()
    {
        var tool = new BashTool { IdleTimeoutSeconds = 60 };
        var command = OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1 > nul" : "sleep 30";
        // Override to 2s via parameter
        var args = MakeArgs($"{{\"command\": \"{command}\", \"idle_timeout_seconds\": 2}}");

        var sw = Stopwatch.StartNew();
        var result = await tool.ExecuteAsync(args, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsError);
        Assert.Contains("idle timeout", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed.TotalSeconds < 15, $"Per-call override should fire quickly, took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task BashTool_IdleTimeout_StderrDoesNotReset()
    {
        var tool = new BashTool { IdleTimeoutSeconds = 2 };
        // Write to stderr only, then sleep — idle timer should still fire
        var command = OperatingSystem.IsWindows()
            ? "echo stderr_only 1>&2 && ping -n 30 127.0.0.1 > nul"
            : "echo stderr_only >&2; sleep 30";
        var args = MakeArgs($"{{\"command\": \"{command}\"}}");

        var sw = Stopwatch.StartNew();
        var result = await tool.ExecuteAsync(args, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsError);
        Assert.Contains("idle timeout", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed.TotalSeconds < 15, $"Stderr should not reset idle, took {sw.Elapsed.TotalSeconds:F1}s");
    }

    // ── CdTool ──

    [Fact]
    public async Task CdTool_ValidDirectory_ChangesDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cd_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            var tool = new CdTool();
            var escapedPath = tempDir.Replace("\\", "\\\\");
            var args = MakeArgs($"{{\"path\": \"{escapedPath}\"}}");
            var result = await tool.ExecuteAsync(args, CancellationToken.None);

            Assert.True(!result.IsError);
            // On macOS, /var is a symlink to /private/var, so GetCurrentDirectory() may
            // return the resolved path. Compare by unique directory name to avoid symlink issues.
            var expectedDirName = Path.GetFileName(tempDir);
            Assert.EndsWith(expectedDirName, Directory.GetCurrentDirectory());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CdTool_InvalidDirectory_ReturnsError()
    {
        var tool = new CdTool();
        var args = MakeArgs("{\"path\": \"/nonexistent/path/that/should/not/exist\"}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    // ── BackgroundProcessManager ──

    [Fact]
    public async Task BackgroundProcessManager_StartAndStop()
    {
        var command = OperatingSystem.IsWindows() ? "ping -t 127.0.0.1" : "sleep 60";
        var managed = BackgroundProcessManager.Start(command, Directory.GetCurrentDirectory());

        Assert.NotNull(managed);
        Assert.False(managed.HasExited);
        Assert.False(string.IsNullOrEmpty(managed.Id));

        BackgroundProcessManager.Stop(managed.Id);

        await Task.Delay(500);
        Assert.True(managed.HasExited);
    }

    [Fact]
    public void BackgroundProcessManager_ListAll_ReturnsProcesses()
    {
        var list = BackgroundProcessManager.ListAll();
        Assert.NotNull(list);
    }

    [Fact]
    public void BackgroundProcessManager_Stop_UnknownId_ReturnsFalse()
    {
        var result = BackgroundProcessManager.Stop("nonexistent_process_id");
        Assert.False(result);
    }

    [Fact]
    public async Task BackgroundProcessManager_GetTailLines_ReturnsOutput()
    {
        var command = OperatingSystem.IsWindows() ? "echo bg-test" : "echo bg-test";
        var managed = BackgroundProcessManager.Start(command, Directory.GetCurrentDirectory());

        // Wait for command to complete
        await Task.Delay(1500);

        var lines = managed.GetTailLines(10);
        Assert.Contains(lines, l => l.Contains("bg-test"));

        BackgroundProcessManager.Stop(managed.Id);
    }
}
