using OpenOrca.Tools.Utilities;
using Xunit;

namespace OpenOrca.Tools.Tests;

public class PathSafetyHelperTests
{
    // ── Drive roots ──

    [Fact]
    public void IsDangerousPath_DriveRoot_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True(PathSafetyHelper.IsDangerousPath(@"C:\"));
    }

    [Fact]
    public void IsDangerousPath_DriveRootForwardSlash_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True(PathSafetyHelper.IsDangerousPath(@"C:/"));
    }

    [Fact]
    public void IsDangerousPath_UnixRoot_ReturnsTrue()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.True(PathSafetyHelper.IsDangerousPath("/"));
    }

    // ── Unix system directories ──

    [Theory]
    [InlineData("/usr")]
    [InlineData("/bin")]
    [InlineData("/sbin")]
    [InlineData("/etc")]
    [InlineData("/var")]
    [InlineData("/lib")]
    [InlineData("/boot")]
    [InlineData("/sys")]
    [InlineData("/proc")]
    [InlineData("/dev")]
    [InlineData("/opt")]
    public void IsDangerousPath_UnixSystemDirs_ReturnsTrue(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.True(PathSafetyHelper.IsDangerousPath(path));
    }

    // ── Windows system directories ──

    [Fact]
    public void IsDangerousPath_WindowsDir_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(winDir))
            Assert.True(PathSafetyHelper.IsDangerousPath(winDir));
    }

    [Fact]
    public void IsDangerousPath_WindowsSystem32_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(winDir))
            Assert.True(PathSafetyHelper.IsDangerousPath(Path.Combine(winDir, "System32")));
    }

    [Fact]
    public void IsDangerousPath_ProgramFiles_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf))
            Assert.True(PathSafetyHelper.IsDangerousPath(pf));
    }

    // ── User profile ──

    [Fact]
    public void IsDangerousPath_UserHome_ReturnsTrue()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            Assert.True(PathSafetyHelper.IsDangerousPath(home));
    }

    [Fact]
    public void IsDangerousPath_UserSubdirectory_ReturnsFalse()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            Assert.False(PathSafetyHelper.IsDangerousPath(Path.Combine(home, "Documents")));
    }

    // ── Safe paths ──

    [Fact]
    public void IsDangerousPath_TempDirectory_ReturnsFalse()
    {
        Assert.False(PathSafetyHelper.IsDangerousPath(Path.GetTempPath()));
    }

    [Fact]
    public void IsDangerousPath_NestedProjectDir_ReturnsFalse()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            Assert.False(PathSafetyHelper.IsDangerousPath(Path.Combine(home, "projects", "myapp")));
    }

    // ── Path traversal ──

    [Fact]
    public void IsDangerousPath_TraversalToRoot_ReturnsTrue()
    {
        // /home/user/../../ resolves to /
        if (OperatingSystem.IsWindows()) return;
        Assert.True(PathSafetyHelper.IsDangerousPath("/home/user/../../"));
    }

    [Fact]
    public void IsDangerousPath_TraversalToEtc_ReturnsTrue()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.True(PathSafetyHelper.IsDangerousPath("/home/user/../../etc"));
    }

    [Fact]
    public void IsDangerousPath_WindowsTraversalToDriveRoot_ReturnsTrue()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True(PathSafetyHelper.IsDangerousPath(@"C:\Users\test\..\.."));
    }
}
