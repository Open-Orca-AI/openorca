using OpenOrca.Core.Session;
using Xunit;

namespace OpenOrca.Core.Tests;

public class CheckpointManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _checkpointDir;
    private readonly CheckpointManager _manager;

    public CheckpointManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openorca_test_{Guid.NewGuid():N}");
        _checkpointDir = Path.Combine(_tempDir, "checkpoints");
        Directory.CreateDirectory(_tempDir);
        _manager = new CheckpointManager(_checkpointDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task Snapshot_CreatesBackupAndManifest()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "original content");

        await _manager.SnapshotAsync(filePath, "session1");

        var entries = await _manager.ListAsync("session1");
        Assert.Single(entries);
        Assert.Equal(Path.GetFullPath(filePath), entries[0].FilePath);
        Assert.True(entries[0].SizeBytes > 0);
    }

    [Fact]
    public async Task Snapshot_SkipsDuplicateForSameSession()
    {
        var filePath = Path.Combine(_tempDir, "dup.txt");
        await File.WriteAllTextAsync(filePath, "content");

        await _manager.SnapshotAsync(filePath, "session1");
        await _manager.SnapshotAsync(filePath, "session1"); // duplicate

        var entries = await _manager.ListAsync("session1");
        Assert.Single(entries);
    }

    [Fact]
    public async Task Snapshot_SkipsNonExistentFile()
    {
        var filePath = Path.Combine(_tempDir, "nonexistent.txt");

        await _manager.SnapshotAsync(filePath, "session1");

        var entries = await _manager.ListAsync("session1");
        Assert.Empty(entries);
    }

    [Fact]
    public async Task Restore_RecoverOriginalContent()
    {
        var filePath = Path.Combine(_tempDir, "restore.txt");
        await File.WriteAllTextAsync(filePath, "original");

        await _manager.SnapshotAsync(filePath, "session1");

        // Modify the file
        await File.WriteAllTextAsync(filePath, "modified");
        Assert.Equal("modified", await File.ReadAllTextAsync(filePath));

        // Restore
        var restored = await _manager.RestoreAsync(filePath, "session1");
        Assert.True(restored);
        Assert.Equal("original", await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task Restore_ReturnsFalseForUncheckpointedFile()
    {
        var filePath = Path.Combine(_tempDir, "nocheckpoint.txt");
        var restored = await _manager.RestoreAsync(filePath, "session1");
        Assert.False(restored);
    }

    [Fact]
    public async Task Diff_ShowsDifferences()
    {
        var filePath = Path.Combine(_tempDir, "diff.txt");
        await File.WriteAllTextAsync(filePath, "line1\nline2\nline3");

        await _manager.SnapshotAsync(filePath, "session1");

        // Modify the file
        await File.WriteAllTextAsync(filePath, "line1\nMODIFIED\nline3");

        var diff = await _manager.DiffAsync(filePath, "session1");
        Assert.Contains("-", diff);
        Assert.Contains("+", diff);
        Assert.Contains("MODIFIED", diff);
    }

    [Fact]
    public async Task Diff_ReturnsNoCheckpointMessage()
    {
        var filePath = Path.Combine(_tempDir, "nodiff.txt");
        var diff = await _manager.DiffAsync(filePath, "session1");
        Assert.Contains("No checkpoint", diff);
    }

    [Fact]
    public async Task Cleanup_RemovesSessionDir()
    {
        var filePath = Path.Combine(_tempDir, "cleanup.txt");
        await File.WriteAllTextAsync(filePath, "content");
        await _manager.SnapshotAsync(filePath, "session1");

        _manager.Cleanup("session1");

        var entries = await _manager.ListAsync("session1");
        Assert.Empty(entries);
    }

    [Fact]
    public async Task List_EmptyForNoCheckpoints()
    {
        var entries = await _manager.ListAsync("nonexistent_session");
        Assert.Empty(entries);
    }

    [Fact]
    public async Task MultipleFiles_IndependentCheckpoints()
    {
        var file1 = Path.Combine(_tempDir, "multi1.txt");
        var file2 = Path.Combine(_tempDir, "multi2.txt");
        await File.WriteAllTextAsync(file1, "content1");
        await File.WriteAllTextAsync(file2, "content2");

        await _manager.SnapshotAsync(file1, "session1");
        await _manager.SnapshotAsync(file2, "session1");

        var entries = await _manager.ListAsync("session1");
        Assert.Equal(2, entries.Count);

        // Modify both
        await File.WriteAllTextAsync(file1, "modified1");
        await File.WriteAllTextAsync(file2, "modified2");

        // Restore only file1
        await _manager.RestoreAsync(file1, "session1");
        Assert.Equal("content1", await File.ReadAllTextAsync(file1));
        Assert.Equal("modified2", await File.ReadAllTextAsync(file2));
    }
}
