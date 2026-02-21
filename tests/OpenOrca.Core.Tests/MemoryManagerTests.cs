using OpenOrca.Core.Configuration;
using Xunit;

namespace OpenOrca.Core.Tests;

public class MemoryManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _globalDir;
    private readonly OrcaConfig _config;
    private readonly MemoryManager _manager;

    public MemoryManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openorca_test_{Guid.NewGuid():N}");
        _globalDir = Path.Combine(_tempDir, "global_memory");
        Directory.CreateDirectory(_tempDir);
        _config = new OrcaConfig();
        // skipProjectDir=true prevents it from finding the real project's .git root
        _manager = new MemoryManager(_config, _globalDir, skipProjectDir: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SaveLearnings_CreatesFile()
    {
        await _manager.SaveLearningsAsync("- Learned pattern A\n- Learned pattern B");

        Assert.True(Directory.Exists(_globalDir));
        var files = Directory.GetFiles(_globalDir, "*.md");
        Assert.Single(files);

        var content = await File.ReadAllTextAsync(files[0]);
        Assert.Contains("Learned pattern A", content);
    }

    [Fact]
    public async Task LoadAllMemory_ReturnsContent()
    {
        await _manager.SaveLearningsAsync("- Test learning 1");

        var loaded = await _manager.LoadAllMemoryAsync();
        Assert.Contains("Test learning 1", loaded);
    }

    [Fact]
    public async Task LoadAllMemory_EmptyWhenNoFiles()
    {
        // Fresh manager with no files saved
        var emptyDir = Path.Combine(_tempDir, "empty_memory");
        var mgr = new MemoryManager(_config, emptyDir, skipProjectDir: true);
        var loaded = await mgr.LoadAllMemoryAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task ListAsync_ReturnsFilesWithPreviews()
    {
        await _manager.SaveLearningsAsync("# First Line Preview\nMore content here");

        var list = await _manager.ListAsync();
        Assert.Single(list);
        Assert.Contains("First Line Preview", list[0].Preview);
    }

    [Fact]
    public async Task ClearAsync_DeletesAllFiles()
    {
        await _manager.SaveLearningsAsync("- Learning 1");
        // Save with different content to get different hash
        await _manager.SaveLearningsAsync("- Learning 2 is different");

        await _manager.ClearAsync();

        var files = Directory.Exists(_globalDir) ? Directory.GetFiles(_globalDir, "*.md") : [];
        Assert.Empty(files);
    }

    [Fact]
    public async Task Prune_DeletesOldestWhenOverMax()
    {
        var pruneConfig = new OrcaConfig { Memory = { MaxMemoryFiles = 2 } };
        var mgr = new MemoryManager(pruneConfig, _globalDir, skipProjectDir: true);

        // Create files with different content/hashes and timestamps
        await mgr.SaveLearningsAsync("Learning A unique content");
        await Task.Delay(50);
        await mgr.SaveLearningsAsync("Learning B different content");
        await Task.Delay(50);
        await mgr.SaveLearningsAsync("Learning C yet another content");

        var files = Directory.GetFiles(_globalDir, "*.md");
        Assert.True(files.Length <= 2, $"Expected at most 2 files, got {files.Length}");
    }

    [Fact]
    public async Task SaveLearnings_EmptyContent_DoesNothing()
    {
        await _manager.SaveLearningsAsync("");
        await _manager.SaveLearningsAsync("  ");

        var dirExists = Directory.Exists(_globalDir);
        if (dirExists)
        {
            var files = Directory.GetFiles(_globalDir, "*.md");
            Assert.Empty(files);
        }
    }
}
