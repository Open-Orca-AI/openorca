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

    // ── BashTool Streaming + Background ──

    [Fact]
    public async Task BashTool_SlowCommand_ReturnsProcessId()
    {
        var tool = new BashTool { IdleTimeoutSeconds = 2 };
        var command = OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1" : "sleep 30";
        var args = MakeArgs($"{{\"command\": \"{command}\"}}");

        var sw = Stopwatch.StartNew();
        var result = await tool.ExecuteAsync(args, CancellationToken.None);
        sw.Stop();

        Assert.False(result.IsError, $"Expected success with process ID but got error: {result.Content}");
        Assert.Contains("still running", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("process ID", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("get_process_output", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed.TotalSeconds < 15, $"Should return within timeout, took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task BashTool_TimeoutOverride_RespectsParameter()
    {
        var tool = new BashTool { IdleTimeoutSeconds = 60 };
        var command = OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1" : "sleep 30";
        var args = MakeArgs($"{{\"command\": \"{command}\", \"timeout_seconds\": 2}}");

        var sw = Stopwatch.StartNew();
        var result = await tool.ExecuteAsync(args, CancellationToken.None);
        sw.Stop();

        Assert.False(result.IsError, $"Expected success with process ID but got error: {result.Content}");
        Assert.Contains("still running", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed.TotalSeconds < 15, $"Per-call override should fire quickly, took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task BashTool_Streaming_CallsCallbackPerLine()
    {
        var tool = new BashTool();
        var command = OperatingSystem.IsWindows()
            ? "echo alpha && echo beta && echo gamma"
            : "echo alpha; echo beta; echo gamma";
        var args = MakeArgs($"{{\"command\": \"{command}\"}}");

        var streamedLines = new List<string>();
        var result = await tool.ExecuteStreamingAsync(args, line => streamedLines.Add(line), CancellationToken.None);

        Assert.False(result.IsError, $"Expected success but got: {result.Content}");
        Assert.True(streamedLines.Count >= 3, $"Expected at least 3 streamed lines but got {streamedLines.Count}: [{string.Join(", ", streamedLines)}]");
        Assert.Contains(streamedLines, l => l.Contains("alpha"));
        Assert.Contains(streamedLines, l => l.Contains("beta"));
        Assert.Contains(streamedLines, l => l.Contains("gamma"));
    }

    [Fact]
    public async Task BashTool_CompletedWithinTimeout_ReturnsFullOutput()
    {
        var tool = new BashTool { IdleTimeoutSeconds = 30 };
        var command = OperatingSystem.IsWindows() ? "echo completed_ok" : "echo completed_ok";
        var args = MakeArgs($"{{\"command\": \"{command}\"}}");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError, $"Expected success but got: {result.Content}");
        Assert.Contains("completed_ok", result.Content);
        Assert.Contains("Exit code: 0", result.Content);
        Assert.DoesNotContain("still running", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BashTool_Timeout_RespectsTimeout()
    {
        var tool = new BashTool();
        var command = OperatingSystem.IsWindows() ? "ping -n 10 127.0.0.1" : "sleep 10";
        var args = MakeArgs($"{{\"command\": \"{command}\", \"timeout_seconds\": 1}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // Now returns process ID instead of "timed out" error
        Assert.Contains("still running", result.Content, StringComparison.OrdinalIgnoreCase);
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

    // ── ManagedProcess.GetNewLines ──

    [Fact]
    public async Task ManagedProcess_GetNewLines_ReturnsDeltaOnly()
    {
        var command = OperatingSystem.IsWindows()
            ? "echo first && echo second && echo third"
            : "echo first; echo second; echo third";
        var managed = BackgroundProcessManager.Start(command, Directory.GetCurrentDirectory());

        await Task.Delay(1500);

        // First call: get all lines
        var cursor = 0;
        var (lines1, cursor1) = managed.GetNewLines(cursor);
        Assert.True(lines1.Count >= 3);
        Assert.Contains(lines1, l => l.Contains("first"));

        // Second call with updated cursor: should return no new lines (process already done)
        var (lines2, cursor2) = managed.GetNewLines(cursor1);
        Assert.Empty(lines2);
        Assert.Equal(cursor1, cursor2);

        BackgroundProcessManager.Stop(managed.Id);
    }

    [Fact]
    public async Task ManagedProcess_WaitForExitAsync_ReturnsTrueOnExit()
    {
        var command = OperatingSystem.IsWindows() ? "echo done" : "echo done";
        var managed = BackgroundProcessManager.Start(command, Directory.GetCurrentDirectory());

        var exited = await managed.WaitForExitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(exited);

        BackgroundProcessManager.Stop(managed.Id);
    }

    [Fact]
    public async Task ManagedProcess_WaitForExitAsync_ReturnsFalseOnTimeout()
    {
        var command = OperatingSystem.IsWindows() ? "ping -t 127.0.0.1" : "sleep 60";
        var managed = BackgroundProcessManager.Start(command, Directory.GetCurrentDirectory());

        var exited = await managed.WaitForExitAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None);
        Assert.False(exited);

        BackgroundProcessManager.Stop(managed.Id);
    }
}
