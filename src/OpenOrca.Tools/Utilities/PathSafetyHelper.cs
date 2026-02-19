namespace OpenOrca.Tools.Utilities;

/// <summary>
/// Validates paths for dangerous filesystem operations (delete, move, copy).
/// Rejects system roots and critical directories.
/// </summary>
public static class PathSafetyHelper
{
    private static readonly HashSet<string> DangerousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/usr",
        "/bin",
        "/sbin",
        "/etc",
        "/var",
        "/lib",
        "/boot",
        "/sys",
        "/proc",
        "/dev",
        "/opt",
    };

    /// <summary>
    /// Returns true if the given path is dangerous to delete/move/overwrite.
    /// </summary>
    public static bool IsDangerousPath(string path)
    {
        path = Path.GetFullPath(path);
        var normalized = path.Replace('\\', '/').TrimEnd('/');

        // Reject explicit dangerous Unix paths
        if (DangerousPaths.Contains(normalized))
            return true;

        // Reject Windows drive roots (C:\, D:\, etc.)
        if (normalized.Length <= 3 && char.IsLetter(normalized[0]) && normalized is [_, ':', '/'])
            return true;
        if (path.Length <= 3 && char.IsLetter(path[0]) && path is [_, ':', '\\'])
            return true;

        // Reject the user profile directory itself (but not subdirectories)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile) &&
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(userProfile), StringComparison.OrdinalIgnoreCase))
            return true;

        // Reject Windows system directories
        var sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(sysRoot) &&
            Path.GetFullPath(path).StartsWith(Path.GetFullPath(sysRoot), StringComparison.OrdinalIgnoreCase))
            return true;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles) &&
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(programFiles), StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
