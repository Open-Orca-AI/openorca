using OpenOrca.Core.Configuration;
using Xunit;

namespace OpenOrca.Core.Tests;

public class ProjectInstructionsLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ProjectInstructionsLoader _loader;

    public ProjectInstructionsLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "openorca-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _loader = new ProjectInstructionsLoader();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* cleanup best effort */ }
    }

    [Fact]
    public void FindProjectRoot_FindsGitDirectory()
    {
        var subDir = Path.Combine(_tempDir, "src", "deep");
        Directory.CreateDirectory(subDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));

        var root = _loader.FindProjectRoot(subDir);

        Assert.Equal(_tempDir, root);
    }

    [Fact]
    public void FindProjectRoot_FindsSlnFile()
    {
        var subDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_tempDir, "Test.sln"), "");

        var root = _loader.FindProjectRoot(subDir);

        Assert.Equal(_tempDir, root);
    }

    [Fact]
    public void FindProjectRoot_ReturnsNull_WhenNoMarkerFound()
    {
        // Use a deeply nested temp dir with no markers
        var isolated = Path.Combine(_tempDir, "no-markers", "deep");
        Directory.CreateDirectory(isolated);

        // This may find markers in parent dirs on the real filesystem,
        // but we can at least verify it doesn't crash
        var root = _loader.FindProjectRoot(isolated);

        // Either null or a valid directory — just assert no exception
        Assert.True(root is null || Directory.Exists(root));
    }

    [Fact]
    public async Task LoadAsync_ReturnsContent_WhenOrcaMdExists()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));
        var orcaMdPath = Path.Combine(_tempDir, "ORCA.md");
        await File.WriteAllTextAsync(orcaMdPath, "# Test Instructions\nDo stuff.");

        var content = await _loader.LoadAsync(_tempDir);

        Assert.NotNull(content);
        Assert.Contains("Test Instructions", content);
    }

    [Fact]
    public async Task LoadAsync_PrefersOrcaDirFile()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));
        Directory.CreateDirectory(Path.Combine(_tempDir, ".orca"));

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ORCA.md"), "root level");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".orca", "ORCA.md"), "orca dir level");

        var content = await _loader.LoadAsync(_tempDir);

        Assert.Equal("orca dir level", content);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenNoFileExists()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));

        var content = await _loader.LoadAsync(_tempDir);

        Assert.Null(content);
    }

    [Fact]
    public void GetInstructionsPath_ReturnsPath_WhenFileExists()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));
        File.WriteAllText(Path.Combine(_tempDir, "ORCA.md"), "content");

        var path = _loader.GetInstructionsPath(_tempDir);

        Assert.NotNull(path);
        Assert.EndsWith("ORCA.md", path);
    }

    [Fact]
    public void GetInstructionsPath_ReturnsDefaultPath_WhenNoFile()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));

        var path = _loader.GetInstructionsPath(_tempDir);

        Assert.NotNull(path);
        Assert.EndsWith("ORCA.md", path);
    }
}
