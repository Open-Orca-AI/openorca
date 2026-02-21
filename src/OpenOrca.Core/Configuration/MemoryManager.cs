using System.Security.Cryptography;
using System.Text;

namespace OpenOrca.Core.Configuration;

/// <summary>
/// Manages auto-learned memory files in ~/.openorca/memory/ (global) and .orca/memory/ (project-level).
/// </summary>
public sealed class MemoryManager
{
    private readonly OrcaConfig _config;
    private readonly string _globalDir;
    private readonly bool _skipProjectDir;

    public MemoryManager(OrcaConfig config)
    {
        _config = config;
        _globalDir = Path.Combine(ConfigManager.GetConfigDirectory(), "memory");
    }

    /// <summary>
    /// Visible for testing — allows overriding the global directory and skipping project dir resolution.
    /// </summary>
    public MemoryManager(OrcaConfig config, string globalDir, bool skipProjectDir = true)
    {
        _config = config;
        _globalDir = globalDir;
        _skipProjectDir = skipProjectDir;
    }

    /// <summary>
    /// Load all memory content from both project and global directories.
    /// Project memory comes first.
    /// </summary>
    public async Task<string> LoadAllMemoryAsync()
    {
        var sb = new StringBuilder();

        // Project-level memory
        var projectDir = GetProjectMemoryDir();
        if (projectDir is not null)
            await AppendMemoryFromDirAsync(projectDir, sb, "Project");

        // Global memory
        await AppendMemoryFromDirAsync(_globalDir, sb, "Global");

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Save learnings to a memory file in the project memory directory.
    /// Falls back to global if no project root found.
    /// </summary>
    public async Task SaveLearningsAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        var dir = GetProjectMemoryDir() ?? _globalDir;
        Directory.CreateDirectory(dir);

        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..6].ToLowerInvariant();
        var fileName = $"{date}-{hash}.md";
        var path = Path.Combine(dir, fileName);

        await File.WriteAllTextAsync(path, content);

        // Prune if exceeding max files
        await PruneAsync(dir);
    }

    /// <summary>
    /// List all memory files with first-line previews.
    /// </summary>
    public async Task<List<(string Path, string Preview)>> ListAsync()
    {
        var results = new List<(string Path, string Preview)>();

        var projectDir = GetProjectMemoryDir();
        if (projectDir is not null)
            await ListFromDirAsync(projectDir, results);

        await ListFromDirAsync(_globalDir, results);

        return results;
    }

    /// <summary>
    /// Delete all auto-generated memory files from both directories.
    /// </summary>
    public Task ClearAsync()
    {
        ClearDir(GetProjectMemoryDir());
        ClearDir(_globalDir);
        return Task.CompletedTask;
    }

    private string? GetProjectMemoryDir()
    {
        if (_skipProjectDir)
            return null;

        var loader = new ProjectInstructionsLoader();
        var projectRoot = loader.FindProjectRoot(Directory.GetCurrentDirectory());
        return projectRoot is not null
            ? Path.Combine(projectRoot, ".orca", "memory")
            : null;
    }

    private static async Task AppendMemoryFromDirAsync(string dir, StringBuilder sb, string label)
    {
        if (!Directory.Exists(dir))
            return;

        var files = Directory.GetFiles(dir, "*.md").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        if (files.Length == 0)
            return;

        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file);
            if (!string.IsNullOrWhiteSpace(content))
            {
                sb.AppendLine(content.Trim());
                sb.AppendLine();
            }
        }
    }

    private static async Task ListFromDirAsync(string dir, List<(string, string)> results)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var file in Directory.GetFiles(dir, "*.md").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var preview = "";
            try
            {
                using var reader = new StreamReader(file);
                preview = (await reader.ReadLineAsync())?.TrimStart('#', ' ') ?? "";
                if (preview.Length > 80)
                    preview = preview[..77] + "...";
            }
            catch { }
            results.Add((file, preview));
        }
    }

    private async Task PruneAsync(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        var files = Directory.GetFiles(dir, "*.md")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        if (files.Length <= _config.Memory.MaxMemoryFiles)
            return;

        foreach (var file in files.Skip(_config.Memory.MaxMemoryFiles))
        {
            try { File.Delete(file); }
            catch { }
        }

        await Task.CompletedTask;
    }

    private static void ClearDir(string? dir)
    {
        if (dir is null || !Directory.Exists(dir))
            return;

        foreach (var file in Directory.GetFiles(dir, "*.md"))
        {
            try { File.Delete(file); }
            catch { }
        }
    }
}
