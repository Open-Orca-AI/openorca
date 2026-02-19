namespace OpenOrca.Core.Configuration;

public sealed class ProjectInstructionsLoader
{
    private static readonly string[] RootMarkers = [".git", "*.sln"];

    /// <summary>
    /// Walk up from startDirectory looking for a project root marker (.git directory or .sln file).
    /// </summary>
    public string? FindProjectRoot(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
            return null;

        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;

            if (Directory.GetFiles(dir.FullName, "*.sln").Length > 0)
                return dir.FullName;

            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Load ORCA.md content from the project root. Checks .orca/ORCA.md first, then ORCA.md.
    /// </summary>
    public async Task<string?> LoadAsync(string startDirectory)
    {
        var root = FindProjectRoot(startDirectory);
        if (root is null) return null;

        // Check .orca/ORCA.md first
        var dotOrcaPath = Path.Combine(root, ".orca", "ORCA.md");
        if (File.Exists(dotOrcaPath))
            return await File.ReadAllTextAsync(dotOrcaPath);

        // Then check ORCA.md at root
        var rootPath = Path.Combine(root, "ORCA.md");
        if (File.Exists(rootPath))
            return await File.ReadAllTextAsync(rootPath);

        return null;
    }

    /// <summary>
    /// Get the path where ORCA.md exists or should be created.
    /// </summary>
    public string? GetInstructionsPath(string startDirectory)
    {
        var root = FindProjectRoot(startDirectory);
        if (root is null) return null;

        var dotOrcaPath = Path.Combine(root, ".orca", "ORCA.md");
        if (File.Exists(dotOrcaPath))
            return dotOrcaPath;

        var rootPath = Path.Combine(root, "ORCA.md");
        if (File.Exists(rootPath))
            return rootPath;

        // Default creation path
        return rootPath;
    }
}
