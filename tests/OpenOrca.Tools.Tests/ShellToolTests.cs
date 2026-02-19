using System.Text.Json;
using OpenOrca.Tools.FileSystem;
using OpenOrca.Tools.Shell;
using Xunit;

namespace OpenOrca.Tools.Tests;

public class ShellToolTests
{
    private static JsonElement MakeArgs(string json) =>
        JsonDocument.Parse(json).RootElement;

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
