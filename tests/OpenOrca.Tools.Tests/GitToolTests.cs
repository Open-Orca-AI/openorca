using System.Text.Json;
using OpenOrca.Tools.Git;
using Xunit;

namespace OpenOrca.Tools.Tests;

public class GitToolTests : IDisposable
{
    private readonly string _tempDir;

    public GitToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openorca_git_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        RunGit("init");
        RunGit("config user.email test@test.com");
        RunGit("config user.name Test");
        // Create initial commit
        File.WriteAllText(Path.Combine(_tempDir, "initial.txt"), "hello");
        RunGit("add .");
        RunGit("commit -m \"initial commit\"");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir)) return;
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

    // ── GitStatusTool ──

    [Fact]
    public async Task GitStatus_CleanRepo_ReturnsStatus()
    {
        var tool = new GitStatusTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        Assert.Contains("## ", result.Content); // Branch line
    }

    [Fact]
    public async Task GitStatus_DirtyRepo_ShowsModifiedFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "new.txt"), "new file");
        var tool = new GitStatusTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        Assert.Contains("new.txt", result.Content);
    }

    [Fact]
    public async Task GitStatus_NotAGitRepo_ReturnsError()
    {
        var nonGitDir = Path.Combine(Path.GetTempPath(), $"notgit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(nonGitDir);
        try
        {
            var tool = new GitStatusTool();
            var args = MakeArgs($"{{\"path\": \"{EscapePath(nonGitDir)}\"}}");
            var result = await tool.ExecuteAsync(args, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains("git repository", result.Content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(nonGitDir, true);
        }
    }

    // ── GitLogTool ──

    [Fact]
    public async Task GitLog_ReturnsCommitHistory()
    {
        var tool = new GitLogTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        Assert.Contains("initial commit", result.Content);
    }

    [Fact]
    public async Task GitLog_WithCount_LimitsResults()
    {
        // Add more commits
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "a");
        RunGit("add .");
        RunGit("commit -m \"second commit\"");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "b");
        RunGit("add .");
        RunGit("commit -m \"third commit\"");

        var tool = new GitLogTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"count\": 1}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        Assert.Contains("third commit", result.Content);
        Assert.DoesNotContain("initial commit", result.Content);
    }

    // ── GitDiffTool ──

    [Fact]
    public async Task GitDiff_NoChanges_ReturnsEmpty()
    {
        var tool = new GitDiffTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
    }

    [Fact]
    public async Task GitDiff_WithChanges_ShowsDiff()
    {
        File.WriteAllText(Path.Combine(_tempDir, "initial.txt"), "modified content");
        var tool = new GitDiffTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        Assert.Contains("modified content", result.Content);
    }

    [Fact]
    public async Task GitDiff_Staged_ShowsStagedChanges()
    {
        File.WriteAllText(Path.Combine(_tempDir, "initial.txt"), "staged change");
        RunGit("add .");
        var tool = new GitDiffTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"staged\": true}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        Assert.Contains("staged change", result.Content);
    }

    // ── GitCommitTool ──

    [Fact]
    public async Task GitCommit_WithMessage_CreatesCommit()
    {
        File.WriteAllText(Path.Combine(_tempDir, "commit_test.txt"), "test content");
        var tool = new GitCommitTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"message\": \"test commit\", \"files\": [\"commit_test.txt\"]}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        Assert.Contains("test commit", result.Content);
    }

    [Fact]
    public async Task GitCommit_StagesAllWhenNoFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "auto_stage.txt"), "auto");
        var tool = new GitCommitTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"message\": \"auto stage commit\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        Assert.Contains("auto stage commit", result.Content);
    }

    // ── GitBranchTool ──

    [Fact]
    public async Task GitBranch_List_ShowsBranches()
    {
        var tool = new GitBranchTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"action\": \"list\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
        // Default branch could be "main" or "master" depending on git config
        Assert.True(
            result.Content.Contains("main") || result.Content.Contains("master"),
            $"Expected branch listing to contain 'main' or 'master', got: {result.Content}");
    }

    [Fact]
    public async Task GitBranch_Create_CreatesBranch()
    {
        var tool = new GitBranchTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"action\": \"create\", \"name\": \"test-branch\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);

        // Verify branch exists
        var listArgs = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"action\": \"list\"}}");
        var listResult = await tool.ExecuteAsync(listArgs, CancellationToken.None);
        Assert.Contains("test-branch", listResult.Content);
    }

    [Fact]
    public async Task GitBranch_Delete_DeletesBranch()
    {
        // Create a branch first
        RunGit("branch delete-me");

        var tool = new GitBranchTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"action\": \"delete\", \"name\": \"delete-me\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
    }

    [Fact]
    public async Task GitBranch_CreateWithoutName_ReturnsError()
    {
        var tool = new GitBranchTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"action\": \"create\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    // ── GitCheckoutTool ──

    [Fact]
    public async Task GitCheckout_SwitchBranch_Works()
    {
        RunGit("branch checkout-target");
        var tool = new GitCheckoutTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"target\": \"checkout-target\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
    }

    [Fact]
    public async Task GitCheckout_CreateAndSwitch_Works()
    {
        var tool = new GitCheckoutTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"target\": \"new-feature\", \"create\": true}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(!result.IsError);
    }

    [Fact]
    public async Task GitCheckout_NonExistentBranch_ReturnsError()
    {
        var tool = new GitCheckoutTool();
        var args = MakeArgs($"{{\"path\": \"{EscapePath(_tempDir)}\", \"target\": \"does-not-exist\"}}");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

}
