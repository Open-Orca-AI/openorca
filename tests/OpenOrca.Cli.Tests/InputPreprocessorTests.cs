using OpenOrca.Cli.Repl;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class InputPreprocessorTests : IDisposable
{
    private readonly string _tempDir;

    public InputPreprocessorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openorca-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void ExpandFileReferences_ValidFile_ExpandsContent()
    {
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "hello world");

        var result = InputPreprocessor.ExpandFileReferences("explain @test.txt", _tempDir);

        Assert.Contains("[File: test.txt]", result);
        Assert.Contains("hello world", result);
        Assert.Contains("[/File]", result);
        Assert.DoesNotContain("@test.txt", result);
    }

    [Fact]
    public void ExpandFileReferences_NonExistentFile_LeftAsIs()
    {
        var result = InputPreprocessor.ExpandFileReferences("explain @nonexistent.txt", _tempDir);

        Assert.Contains("@nonexistent.txt", result);
        Assert.DoesNotContain("[File:", result);
    }

    [Fact]
    public void ExpandFileReferences_MultipleFiles_AllExpanded()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "content a");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "content b");

        var result = InputPreprocessor.ExpandFileReferences("compare @a.txt and @b.txt", _tempDir);

        Assert.Contains("content a", result);
        Assert.Contains("content b", result);
        Assert.DoesNotContain("@a.txt", result);
        Assert.DoesNotContain("@b.txt", result);
    }

    [Fact]
    public void ExpandFileReferences_EmailLikePattern_LeftAsIs()
    {
        // user@email.com won't resolve to a file
        var result = InputPreprocessor.ExpandFileReferences("contact user@email.com for help", _tempDir);

        Assert.DoesNotContain("[File:", result);
    }

    [Fact]
    public void ExpandFileReferences_LargeFile_Truncated()
    {
        var largeContent = new string('x', 60_000);
        File.WriteAllText(Path.Combine(_tempDir, "large.txt"), largeContent);

        var result = InputPreprocessor.ExpandFileReferences("read @large.txt", _tempDir);

        Assert.Contains("[File: large.txt]", result);
        Assert.Contains("... (truncated)", result);
        Assert.True(result.Length < 55_000);
    }

    [Fact]
    public void ExpandFileReferences_NoAtSign_ReturnsUnchanged()
    {
        var input = "just a normal message";
        var result = InputPreprocessor.ExpandFileReferences(input, _tempDir);

        Assert.Equal(input, result);
    }

    [Fact]
    public void ExpandFileReferences_EmptyInput_ReturnsUnchanged()
    {
        var result = InputPreprocessor.ExpandFileReferences("", _tempDir);
        Assert.Equal("", result);
    }

    [Fact]
    public void ExpandFileReferences_SubDirectory_ExpandsCorrectly()
    {
        var subDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "main.cs"), "class Main {}");

        var result = InputPreprocessor.ExpandFileReferences("review @src/main.cs", _tempDir);

        Assert.Contains("class Main {}", result);
        Assert.DoesNotContain("@src/main.cs", result);
    }
}
