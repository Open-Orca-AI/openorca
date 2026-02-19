using System.Text.Json;
using OpenOrca.Tools.Git;
using Xunit;

namespace OpenOrca.Tools.Tests;

public class GitRemoteToolTests : IDisposable
{
    private readonly string _tempDir;

    public GitRemoteToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openorca_git_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        // Initialize a git repo
        RunGit("init");
        RunGit("config user.email test@test.com");
        RunGit("config user.name Test");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir)) return;

        // Git objects are read-only on Windows — remove the attribute before deleting
        foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
        }
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private void RunGit(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = _tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();
    }

    private static JsonElement MakeArgs(string json) =>
        JsonDocument.Parse(json).RootElement;

    private string EscapePath(string path) =>
        path.Replace("\\", "\\\\");

    // ── GitStashTool ──

    [Fact]
    public async Task GitStashTool_ListOnEmptyRepo()
    {
        // Create initial commit so stash has a base
        File.WriteAllText(Path.Combine(_tempDir, "init.txt"), "init");
        RunGit("add -A");
        RunGit("commit -m initial");

        var tool = new GitStashTool();
        var args = MakeArgs($$"""{"action": "list", "path": "{{EscapePath(_tempDir)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // No stashes, should succeed with empty output
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task GitStashTool_PushAndPop()
    {
        // Create initial commit
        File.WriteAllText(Path.Combine(_tempDir, "init.txt"), "init");
        RunGit("add -A");
        RunGit("commit -m initial");

        // Create uncommitted changes
        File.WriteAllText(Path.Combine(_tempDir, "changed.txt"), "modified");
        RunGit("add -A");

        var tool = new GitStashTool();

        // Push
        var pushArgs = MakeArgs($$"""{"action": "push", "message": "test stash", "path": "{{EscapePath(_tempDir)}}"}""");
        var pushResult = await tool.ExecuteAsync(pushArgs, CancellationToken.None);
        Assert.False(pushResult.IsError);

        // List — should have one stash
        var listArgs = MakeArgs($$"""{"action": "list", "path": "{{EscapePath(_tempDir)}}"}""");
        var listResult = await tool.ExecuteAsync(listArgs, CancellationToken.None);
        Assert.False(listResult.IsError);
        Assert.Contains("test stash", listResult.Content);

        // Pop
        var popArgs = MakeArgs($$"""{"action": "pop", "index": 0, "path": "{{EscapePath(_tempDir)}}"}""");
        var popResult = await tool.ExecuteAsync(popArgs, CancellationToken.None);
        Assert.False(popResult.IsError);
    }

    // ── GitPushTool ──

    [Fact]
    public async Task GitPushTool_FailsWithoutRemote()
    {
        File.WriteAllText(Path.Combine(_tempDir, "init.txt"), "init");
        RunGit("add -A");
        RunGit("commit -m initial");

        var tool = new GitPushTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(_tempDir)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // Should fail because no remote is configured
        Assert.True(result.IsError);
    }

    // ── GitPullTool ──

    [Fact]
    public async Task GitPullTool_FailsWithoutRemote()
    {
        File.WriteAllText(Path.Combine(_tempDir, "init.txt"), "init");
        RunGit("add -A");
        RunGit("commit -m initial");

        var tool = new GitPullTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(_tempDir)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // Should fail because no remote is configured
        Assert.True(result.IsError);
    }
}
