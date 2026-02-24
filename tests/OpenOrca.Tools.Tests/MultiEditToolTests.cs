using OpenOrca.Tools.FileSystem;
using Xunit;
using static OpenOrca.Tools.Tests.TestHelpers;

namespace OpenOrca.Tools.Tests;

public class MultiEditToolTests : IDisposable
{
    private readonly string _tempDir;

    public MultiEditToolTests()
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
    public async Task MultiEdit_AppliesEditsToMultipleFiles()
    {
        var file1 = Path.Combine(_tempDir, "file1.txt");
        var file2 = Path.Combine(_tempDir, "file2.txt");
        await File.WriteAllTextAsync(file1, "Hello World");
        await File.WriteAllTextAsync(file2, "Foo Bar Baz");

        var tool = new MultiEditTool();
        var args = MakeArgs($$"""
        {
            "edits": [
                { "path": "{{EscapePath(file1)}}", "old_string": "World", "new_string": "OpenOrca" },
                { "path": "{{EscapePath(file2)}}", "old_string": "Bar", "new_string": "Qux" }
            ]
        }
        """);

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Hello OpenOrca", await File.ReadAllTextAsync(file1));
        Assert.Equal("Foo Qux Baz", await File.ReadAllTextAsync(file2));
        Assert.Contains("2 edit(s)", result.Content);
    }

    [Fact]
    public async Task MultiEdit_RollsBackOnFailure()
    {
        var file1 = Path.Combine(_tempDir, "rollback1.txt");
        var file2 = Path.Combine(_tempDir, "rollback2.txt");
        await File.WriteAllTextAsync(file1, "Original1");
        await File.WriteAllTextAsync(file2, "Original2");

        var tool = new MultiEditTool();
        // Second edit references a string not in file2
        var args = MakeArgs($$"""
        {
            "edits": [
                { "path": "{{EscapePath(file1)}}", "old_string": "Original1", "new_string": "Modified1" },
                { "path": "{{EscapePath(file2)}}", "old_string": "NONEXISTENT", "new_string": "Modified2" }
            ]
        }
        """);

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content);
        // Validation fails before writing, so originals should be untouched
        Assert.Equal("Original1", await File.ReadAllTextAsync(file1));
        Assert.Equal("Original2", await File.ReadAllTextAsync(file2));
    }

    [Fact]
    public async Task MultiEdit_ReplaceAll_ReplacesAllOccurrences()
    {
        var file1 = Path.Combine(_tempDir, "replaceall.txt");
        await File.WriteAllTextAsync(file1, "aaa bbb aaa ccc aaa");

        var tool = new MultiEditTool();
        var args = MakeArgs($$"""
        {
            "edits": [
                { "path": "{{EscapePath(file1)}}", "old_string": "aaa", "new_string": "zzz", "replace_all": true }
            ]
        }
        """);

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("zzz bbb zzz ccc zzz", await File.ReadAllTextAsync(file1));
    }

    [Fact]
    public async Task MultiEdit_EmptyEditsArray_ReturnsError()
    {
        var tool = new MultiEditTool();
        var args = MakeArgs("""{ "edits": [] }""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("empty", result.Content);
    }

    [Fact]
    public async Task MultiEdit_MultipleEditsToSameFile()
    {
        var file1 = Path.Combine(_tempDir, "multi.txt");
        await File.WriteAllTextAsync(file1, "alpha beta gamma");

        var tool = new MultiEditTool();
        var args = MakeArgs($$"""
        {
            "edits": [
                { "path": "{{EscapePath(file1)}}", "old_string": "alpha", "new_string": "ALPHA" },
                { "path": "{{EscapePath(file1)}}", "old_string": "gamma", "new_string": "GAMMA" }
            ]
        }
        """);

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("ALPHA beta GAMMA", await File.ReadAllTextAsync(file1));
    }

    [Fact]
    public async Task MultiEdit_ErrorsOnNonUniqueString()
    {
        var file1 = Path.Combine(_tempDir, "nonunique.txt");
        await File.WriteAllTextAsync(file1, "hello hello hello");

        var tool = new MultiEditTool();
        var args = MakeArgs($$"""
        {
            "edits": [
                { "path": "{{EscapePath(file1)}}", "old_string": "hello", "new_string": "world" }
            ]
        }
        """);

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("3 times", result.Content);
    }

    [Fact]
    public async Task MultiEdit_ErrorsOnMissingFile()
    {
        var tool = new MultiEditTool();
        var args = MakeArgs("""
        {
            "edits": [
                { "path": "/nonexistent/file.txt", "old_string": "a", "new_string": "b" }
            ]
        }
        """);

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content.ToLower());
    }
}
