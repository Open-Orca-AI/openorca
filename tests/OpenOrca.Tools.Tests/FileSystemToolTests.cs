using OpenOrca.Tools.FileSystem;
using Xunit;
using static OpenOrca.Tools.Tests.TestHelpers;

namespace OpenOrca.Tools.Tests;

public class FileSystemToolTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openorca_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ReadFileTool_ReadsExistingFile()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "line1\nline2\nline3");

        var tool = new ReadFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("line1", result.Content);
        Assert.Contains("line2", result.Content);
        Assert.Contains("line3", result.Content);
    }

    [Fact]
    public async Task ReadFileTool_ReturnsError_ForMissingFile()
    {
        var tool = new ReadFileTool();
        var args = MakeArgs("""{"path": "/nonexistent/file.txt"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content);
    }

    [Fact]
    public async Task WriteFileTool_CreatesNewFile()
    {
        var filePath = Path.Combine(_tempDir, "new_file.txt");

        var tool = new WriteFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "content": "Hello World"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(File.Exists(filePath));
        Assert.Equal("Hello World", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task WriteFileTool_CreatesParentDirectories()
    {
        var filePath = Path.Combine(_tempDir, "sub", "dir", "file.txt");

        var tool = new WriteFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "content": "nested"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task EditFileTool_ReplacesUniqueString()
    {
        var filePath = Path.Combine(_tempDir, "edit.txt");
        await File.WriteAllTextAsync(filePath, "Hello World, Hello!");

        var tool = new EditFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "old_string": "World", "new_string": "OpenOrca"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Hello OpenOrca, Hello!", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task EditFileTool_ErrorsOnNonUniqueString()
    {
        var filePath = Path.Combine(_tempDir, "edit2.txt");
        await File.WriteAllTextAsync(filePath, "Hello Hello");

        var tool = new EditFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "old_string": "Hello", "new_string": "Hi"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("appears 2 times", result.Content);
    }

    [Fact]
    public async Task EditFileTool_ReplaceAll_ReplacesAllOccurrences()
    {
        var filePath = Path.Combine(_tempDir, "edit3.txt");
        await File.WriteAllTextAsync(filePath, "Hello Hello Hello");

        var tool = new EditFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "old_string": "Hello", "new_string": "Hi", "replace_all": true}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Hi Hi Hi", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task ListDirectoryTool_ListsContents()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "");
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));

        var tool = new ListDirectoryTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(_tempDir)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("a.txt", result.Content);
        Assert.Contains("b.txt", result.Content);
        Assert.Contains("subdir/", result.Content);
    }

    [Fact]
    public async Task GlobTool_FindsMatchingFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "test.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "");

        var tool = new GlobTool();
        var args = MakeArgs($$"""{"pattern": "*.cs", "path": "{{EscapePath(_tempDir)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("test.cs", result.Content);
        Assert.DoesNotContain("test.txt", result.Content);
    }

    [Fact]
    public async Task GrepTool_FindsMatchingContent()
    {
        File.WriteAllText(Path.Combine(_tempDir, "search.cs"), "public class Foo\n{\n    void Bar() { }\n}\n");

        var tool = new GrepTool();
        var args = MakeArgs($$"""{"pattern": "class Foo", "path": "{{EscapePath(_tempDir)}}", "glob": "*.cs"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("class Foo", result.Content);
    }

    // ── New feature tests ──

    [Fact]
    public async Task WriteFileTool_AppendsContent()
    {
        var filePath = Path.Combine(_tempDir, "append.txt");
        await File.WriteAllTextAsync(filePath, "Line1\n");

        var tool = new WriteFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "content": "Line2\n", "append": true}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("Appended", result.Content);
        Assert.Equal("Line1\nLine2\n", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task EditFileTool_CreateIfMissing_CreatesFile()
    {
        var filePath = Path.Combine(_tempDir, "brand_new.txt");

        var tool = new EditFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "old_string": "", "new_string": "new content", "create_if_missing": true}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("Created", result.Content);
        Assert.True(File.Exists(filePath));
        Assert.Equal("new content", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task EditFileTool_CreateIfMissing_ErrorsIfOldStringNotEmpty()
    {
        var filePath = Path.Combine(_tempDir, "should_not_exist.txt");

        var tool = new EditFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}", "old_string": "something", "new_string": "new", "create_if_missing": true}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // old_string is non-empty but file doesn't exist — should error
        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content.ToLower());
    }

    [Fact]
    public async Task ReadFileTool_DetectsBinaryFile()
    {
        var filePath = Path.Combine(_tempDir, "binary.bin");
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00 };
        await File.WriteAllBytesAsync(filePath, bytes);

        var tool = new ReadFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Binary file", result.Content);
    }

    [Fact]
    public async Task ReadFileTool_ShowsFileInfoHeader()
    {
        var filePath = Path.Combine(_tempDir, "header_test.txt");
        await File.WriteAllTextAsync(filePath, "a\nb\nc\nd\ne");

        var tool = new ReadFileTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(filePath)}}"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("5 lines", result.Content);
        Assert.Contains("showing", result.Content);
    }

    [Fact]
    public async Task GrepTool_FilesOnlyMode()
    {
        File.WriteAllText(Path.Combine(_tempDir, "match1.cs"), "class Foo {}");
        File.WriteAllText(Path.Combine(_tempDir, "match2.cs"), "class Foo {}");

        var tool = new GrepTool();
        var args = MakeArgs($$"""{"pattern": "class Foo", "path": "{{EscapePath(_tempDir)}}", "glob": "*.cs", "output_mode": "files_only"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("match1.cs", result.Content);
        Assert.Contains("match2.cs", result.Content);
        Assert.Contains("2 files", result.Content);
    }

    [Fact]
    public async Task GrepTool_CountMode()
    {
        File.WriteAllText(Path.Combine(_tempDir, "counts.cs"), "foo\nfoo\nbar\nfoo\n");

        var tool = new GrepTool();
        var args = MakeArgs($$"""{"pattern": "foo", "path": "{{EscapePath(_tempDir)}}", "glob": "*.cs", "output_mode": "count"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("3", result.Content);
    }

    [Fact]
    public async Task GlobTool_ExcludePattern()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "bin"));
        File.WriteAllText(Path.Combine(_tempDir, "src", "app.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "bin", "app.cs"), "");

        var tool = new GlobTool();
        var args = MakeArgs($$"""{"pattern": "**/*.cs", "path": "{{EscapePath(_tempDir)}}", "exclude": "bin/**"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("src", result.Content);
        Assert.DoesNotContain("bin", result.Content);
    }

    [Fact]
    public async Task ListDirectoryTool_RecursiveMode()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "a"));
        File.WriteAllText(Path.Combine(_tempDir, "a", "nested.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "top.txt"), "");

        var tool = new ListDirectoryTool();
        var args = MakeArgs($$"""{"path": "{{EscapePath(_tempDir)}}", "recursive": true}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("nested.txt", result.Content);
        Assert.Contains("top.txt", result.Content);
    }
}
