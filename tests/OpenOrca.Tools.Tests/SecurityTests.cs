using OpenOrca.Tools.FileSystem;
using OpenOrca.Tools.Shell;
using Xunit;
using static OpenOrca.Tools.Tests.TestHelpers;

namespace OpenOrca.Tools.Tests;

/// <summary>
/// Security-focused test cases for tool inputs — verifies that tools reject
/// dangerous inputs, oversized payloads, and path manipulation attempts.
/// </summary>
public class SecurityTests : IDisposable
{
    private readonly string _tempDir;

    public SecurityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openorca_sec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── WriteFileTool security ──

    [Fact]
    public async Task WriteFileTool_RejectsEmptyContent()
    {
        var tool = new WriteFileTool();
        var filePath = Path.Combine(_tempDir, "empty.txt");
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "content": "   "}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("empty", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteFileTool_RejectsRoleTagContent()
    {
        var tool = new WriteFileTool();
        var filePath = Path.Combine(_tempDir, "role.txt");
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "content": "</assistant>"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("role tag", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteFileTool_AllowsLegitimateContentWithRoleTags()
    {
        var tool = new WriteFileTool();
        var filePath = Path.Combine(_tempDir, "legit.txt");
        // Content over 50 chars that happens to contain a role tag — should be allowed
        var content = "This is a legitimate file with some content that discusses <assistant> role tags in LLM systems.";
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "content": "{{content}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
    }

    // ── DeleteFileTool security ──

    [Fact]
    public async Task DeleteFileTool_RejectsUserHomeDirectory()
    {
        var tool = new DeleteFileTool();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return;

        var args = MakeArgs($$"""{"path": "{{EscapePath(home)}}"}""");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("dangerous", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteFileTool_RejectsPathTraversal()
    {
        var tool = new DeleteFileTool();
        // Attempt to traverse to a dangerous path
        string dangerousPath;
        if (OperatingSystem.IsWindows())
        {
            dangerousPath = @"C:\Users\test\..\..";
        }
        else
        {
            dangerousPath = "/home/user/../../etc";
        }
        var args = MakeArgs($$"""{"path": "{{EscapePath(dangerousPath)}}"}""");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    // ── MoveFileTool security ──

    [Fact]
    public async Task MoveFileTool_RejectsDangerousSource()
    {
        var tool = new MoveFileTool();
        var dangerousPath = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            : "/etc";
        if (string.IsNullOrEmpty(dangerousPath)) return;

        var args = MakeArgs($$"""{"source": "{{EscapePath(dangerousPath)}}", "destination": "{{EscapePath(_tempDir)}}"}""");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("dangerous", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    // ── CopyFileTool path traversal ──

    [Fact]
    public async Task CopyFileTool_RejectsNonexistentSource()
    {
        var tool = new CopyFileTool();
        var args = MakeArgs($$"""{"source": "/nonexistent/path", "destination": "{{EscapePath(_tempDir)}}"}""");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    // ── ReadFileTool security ──

    [Fact]
    public async Task ReadFileTool_RejectsNonexistentFile()
    {
        var tool = new ReadFileTool();
        var args = MakeArgs("""{"path": "/nonexistent/secret/file.txt"}""");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    // ── BashTool security ──

    [Fact]
    public async Task BashTool_HandlesEmptyCommand()
    {
        var tool = new BashTool();
        var args = MakeArgs("""{"command": ""}""");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // Empty command should either error or return empty output, not crash
        Assert.NotNull(result);
    }

    [Fact]
    public async Task BashTool_RespectsTimeout()
    {
        var tool = new BashTool();
        // Use a 1-second timeout with a command that would run longer
        var command = OperatingSystem.IsWindows() ? "timeout /t 30 /nobreak" : "sleep 30";
        var args = MakeArgs($$"""{"command": "{{command}}", "timeout_seconds": 2}""");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await tool.ExecuteAsync(args, cts.Token);

        // Should complete (via timeout) rather than hang for 30 seconds
        Assert.NotNull(result);
    }

    // ── GlobTool security ──

    [Fact]
    public async Task GlobTool_HandlesNonexistentDirectory()
    {
        var tool = new GlobTool();
        var args = MakeArgs("""{"pattern": "*.cs", "path": "/nonexistent/directory"}""");
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // Should handle gracefully — either error or empty results
        Assert.NotNull(result);
    }
}
