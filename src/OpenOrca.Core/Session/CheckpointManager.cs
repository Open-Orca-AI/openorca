using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Serialization;

namespace OpenOrca.Core.Session;

public sealed class CheckpointEntry
{
    public string FilePath { get; set; } = "";
    public string BackupFile { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class CheckpointManager
{
    private readonly string _baseDir;

    public CheckpointManager()
    {
        _baseDir = Path.Combine(ConfigManager.GetConfigDirectory(), "checkpoints");
    }

    /// <summary>
    /// Visible for testing — allows overriding the checkpoint storage directory.
    /// </summary>
    public CheckpointManager(string baseDir)
    {
        _baseDir = baseDir;
    }

    private string GetSessionDir(string sessionId)
        => Path.Combine(_baseDir, sessionId);

    private string GetManifestPath(string sessionId)
        => Path.Combine(GetSessionDir(sessionId), "manifest.json");

    /// <summary>
    /// Snapshot a file before modification. Only snapshots the first time per file per session.
    /// </summary>
    public async Task SnapshotAsync(string filePath, string sessionId)
    {
        filePath = Path.GetFullPath(filePath);

        if (!File.Exists(filePath))
            return;

        var sessionDir = GetSessionDir(sessionId);
        Directory.CreateDirectory(sessionDir);

        // Load existing manifest
        var manifest = await LoadManifestAsync(sessionId);

        // Skip if already checkpointed this file in this session
        if (manifest.Any(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            return;

        // Create backup
        var timestamp = DateTime.UtcNow;
        var pathHash = ComputeHash(filePath);
        var backupFileName = $"{timestamp:yyyyMMdd_HHmmss}_{pathHash}.bak";
        var backupPath = Path.Combine(sessionDir, backupFileName);

        await CopyFileAsync(filePath, backupPath);

        var fileInfo = new FileInfo(filePath);
        manifest.Add(new CheckpointEntry
        {
            FilePath = filePath,
            BackupFile = backupFileName,
            Timestamp = timestamp,
            SizeBytes = fileInfo.Length
        });

        await SaveManifestAsync(sessionId, manifest);
    }

    /// <summary>
    /// List all checkpointed files for a session.
    /// </summary>
    public async Task<List<CheckpointEntry>> ListAsync(string sessionId)
    {
        return await LoadManifestAsync(sessionId);
    }

    /// <summary>
    /// Get a unified diff between the checkpoint and the current file.
    /// </summary>
    public async Task<string> DiffAsync(string filePath, string sessionId)
    {
        filePath = Path.GetFullPath(filePath);
        var manifest = await LoadManifestAsync(sessionId);
        var entry = manifest.FirstOrDefault(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return $"No checkpoint found for: {filePath}";

        var backupPath = Path.Combine(GetSessionDir(sessionId), entry.BackupFile);
        if (!File.Exists(backupPath))
            return "Checkpoint backup file is missing.";

        var originalLines = (await File.ReadAllTextAsync(backupPath)).Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var currentLines = File.Exists(filePath)
            ? (await File.ReadAllTextAsync(filePath)).Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
            : [];

        return GenerateSimpleDiff(originalLines, currentLines, filePath);
    }

    /// <summary>
    /// Restore a file from its checkpoint.
    /// </summary>
    public async Task<bool> RestoreAsync(string filePath, string sessionId)
    {
        filePath = Path.GetFullPath(filePath);
        var manifest = await LoadManifestAsync(sessionId);
        var entry = manifest.FirstOrDefault(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return false;

        var backupPath = Path.Combine(GetSessionDir(sessionId), entry.BackupFile);
        if (!File.Exists(backupPath))
            return false;

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await CopyFileAsync(backupPath, filePath);
        return true;
    }

    /// <summary>
    /// Delete all checkpoints for a session.
    /// </summary>
    public void Cleanup(string sessionId)
    {
        var sessionDir = GetSessionDir(sessionId);
        if (Directory.Exists(sessionDir))
            Directory.Delete(sessionDir, true);
    }

    private async Task<List<CheckpointEntry>> LoadManifestAsync(string sessionId)
    {
        var path = GetManifestPath(sessionId);
        if (!File.Exists(path))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize(json, OrcaJsonContext.Default.ListCheckpointEntry) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveManifestAsync(string sessionId, List<CheckpointEntry> manifest)
    {
        var path = GetManifestPath(sessionId);
        var json = JsonSerializer.Serialize(manifest, OrcaJsonContext.Default.ListCheckpointEntry);
        await File.WriteAllTextAsync(path, json);
    }

    private static async Task CopyFileAsync(string source, string destination)
    {
        const int bufferSize = 81920;
        await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        await sourceStream.CopyToAsync(destStream);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    private static string GenerateSimpleDiff(string[] original, string[] current, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"--- {filePath} (checkpoint)");
        sb.AppendLine($"+++ {filePath} (current)");

        var maxLines = Math.Max(original.Length, current.Length);
        var inHunk = false;

        for (var i = 0; i < maxLines; i++)
        {
            var origLine = i < original.Length ? original[i] : null;
            var currLine = i < current.Length ? current[i] : null;

            if (origLine != currLine)
            {
                if (!inHunk)
                {
                    sb.AppendLine($"@@ line {i + 1} @@");
                    inHunk = true;
                }
                if (origLine is not null)
                    sb.AppendLine($"- {origLine}");
                if (currLine is not null)
                    sb.AppendLine($"+ {currLine}");
            }
            else
            {
                inHunk = false;
            }
        }

        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no differences)";
    }
}
