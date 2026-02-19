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
        var resolved = ResolveFinalPath(path);

        return IsDangerousResolved(resolved);
    }

    private static string ResolveFinalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);

        // Resolve symlinks and junctions to their final target
        try
        {
            if (Directory.Exists(fullPath))
            {
                var target = new DirectoryInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                    return Path.GetFullPath(target.FullName);
            }
            else if (File.Exists(fullPath))
            {
                var target = new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                    return Path.GetFullPath(target.FullName);
            }
        }
        catch
        {
            // If symlink resolution fails, fall through to use the unresolved path
        }

        return fullPath;
    }

    private static bool IsDangerousResolved(string fullPath)
    {
        var normalized = fullPath.Replace('\\', '/').TrimEnd('/');

        // Reject explicit dangerous Unix paths
        if (DangerousPaths.Contains(normalized))
            return true;

        // Reject drive roots (C:\, D:\, /)
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(fullPath.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return true;

        // Reject the user profile directory itself (but not subdirectories)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile) &&
            string.Equals(fullPath, Path.GetFullPath(userProfile), StringComparison.OrdinalIgnoreCase))
            return true;

        // Reject Windows system directories
        var sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(sysRoot) &&
            fullPath.StartsWith(Path.GetFullPath(sysRoot), StringComparison.OrdinalIgnoreCase))
            return true;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles) &&
            string.Equals(fullPath, Path.GetFullPath(programFiles), StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
