using OpenOrca.Tools.FileSystem;
using Xunit;
using static OpenOrca.Tools.Tests.TestHelpers;

namespace OpenOrca.Tools.Tests;

public class DeleteMoveToolTests : IDisposable
{
    private readonly string _tempDir;

    public DeleteMoveToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openorca_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── DeleteFileTool ──

    [Fact]
    public async Task DeleteFileTool_DeletesFile()
    {
        var filePath = Path.Combine(_tempDir, "to_delete.txt");
        await File.WriteAllTextAsync(filePath, "content");

        var tool = new DeleteFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteFileTool_DeletesEmptyDirectory()
    {
        var dirPath = Path.Combine(_tempDir, "empty_dir");
        Directory.CreateDirectory(dirPath);

        var tool = new DeleteFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(dirPath)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(Directory.Exists(dirPath));
    }

    [Fact]
    public async Task DeleteFileTool_RequiresRecursiveForNonEmptyDir()
    {
        var dirPath = Path.Combine(_tempDir, "nonempty_dir");
        Directory.CreateDirectory(dirPath);
        await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "data");

        var tool = new DeleteFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(dirPath)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("recursive", result.Content);
    }

    [Fact]
    public async Task DeleteFileTool_RecursiveDeletesNonEmptyDir()
    {
        var dirPath = Path.Combine(_tempDir, "nonempty_dir2");
        Directory.CreateDirectory(dirPath);
        await File.WriteAllTextAsync(Path.Combine(dirPath, "file.txt"), "data");

        var tool = new DeleteFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(dirPath)}}", "recursive": true}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(Directory.Exists(dirPath));
    }

    [Fact]
    public async Task DeleteFileTool_RejectsNotFound()
    {
        var tool = new DeleteFileTool();
        var args = MakeArgs("""{"path": "/nonexistent/path/xyz"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content);
    }

    [Fact]
    public async Task DeleteFileTool_RejectsDangerousPath()
    {
        var tool = new DeleteFileTool();
        // Use a platform-appropriate dangerous path: Windows dir on Windows, /etc on Linux/macOS
        var dangerousPath = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            : "/etc";
        var args = MakeArgs($$"""{"path": "{{EscapePath(dangerousPath)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("dangerous", result.Content.ToLower());
    }

    // ── MoveFileTool ──

    [Fact]
    public async Task MoveFileTool_MovesFile()
    {
        var src = Path.Combine(_tempDir, "src.txt");
        var dst = Path.Combine(_tempDir, "dst.txt");
        await File.WriteAllTextAsync(src, "content");

        var tool = new MoveFileTool();
        var args = MakeArgs($$"""{"source": "{{EscapePath(src)}}", "destination": "{{EscapePath(dst)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dst));
        Assert.Equal("content", await File.ReadAllTextAsync(dst));
    }

    [Fact]
    public async Task MoveFileTool_CreatesParentDirs()
    {
        var src = Path.Combine(_tempDir, "move_src.txt");
        var dst = Path.Combine(_tempDir, "sub", "dir", "move_dst.txt");
        await File.WriteAllTextAsync(src, "data");

        var tool = new MoveFileTool();
        var args = MakeArgs($$"""{"source": "{{EscapePath(src)}}", "destination": "{{EscapePath(dst)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(File.Exists(dst));
    }

    // ── CopyFileTool ──

    [Fact]
    public async Task CopyFileTool_CopiesFile()
    {
        var src = Path.Combine(_tempDir, "copy_src.txt");
        var dst = Path.Combine(_tempDir, "copy_dst.txt");
        await File.WriteAllTextAsync(src, "copy me");

        var tool = new CopyFileTool();
        var args = MakeArgs($$"""{"source": "{{EscapePath(src)}}", "destination": "{{EscapePath(dst)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(File.Exists(src));  // source still exists
        Assert.True(File.Exists(dst));
        Assert.Equal("copy me", await File.ReadAllTextAsync(dst));
    }

    [Fact]
    public async Task CopyFileTool_RequiresRecursiveForDirs()
    {
        var srcDir = Path.Combine(_tempDir, "copy_dir_src");
        var dstDir = Path.Combine(_tempDir, "copy_dir_dst");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "file.txt"), "data");

        var tool = new CopyFileTool();
        var args = MakeArgs($$"""{"source": "{{EscapePath(srcDir)}}", "destination": "{{EscapePath(dstDir)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("recursive", result.Content);
    }

    [Fact]
    public async Task CopyFileTool_RecursiveCopiesDir()
    {
        var srcDir = Path.Combine(_tempDir, "copy_dir_src2");
        var dstDir = Path.Combine(_tempDir, "copy_dir_dst2");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "file.txt"), "nested data");
        Directory.CreateDirectory(Path.Combine(srcDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(srcDir, "sub", "deep.txt"), "deep");

        var tool = new CopyFileTool();
        var args = MakeArgs($$"""{"source": "{{EscapePath(srcDir)}}", "destination": "{{EscapePath(dstDir)}}", "recursive": true}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(Directory.Exists(dstDir));
        Assert.True(File.Exists(Path.Combine(dstDir, "file.txt")));
        Assert.True(File.Exists(Path.Combine(dstDir, "sub", "deep.txt")));
    }
}
