using OpenOrca.Cli.CustomCommands;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class CustomCommandLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public CustomCommandLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"openorca_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task LoadTemplateAsync_ReadsFileContent()
    {
        // Create a commands dir inside a project root (simulating .orca/commands/)
        var orcaDir = Path.Combine(_tempDir, ".orca", "commands");
        Directory.CreateDirectory(orcaDir);
        var templatePath = Path.Combine(orcaDir, "review-pr.md");
        await File.WriteAllTextAsync(templatePath, "Review PR #{{ARG1}} with focus on {{ARGS}}");

        // Create a .git dir so it's detected as project root
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));

        // Set CWD to our temp project
        var origCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir);
        try
        {
            var loader = new CustomCommandLoader();
            var template = await loader.LoadTemplateAsync("review-pr");

            Assert.NotNull(template);
            Assert.Contains("{{ARG1}}", template);
            Assert.Contains("{{ARGS}}", template);
        }
        finally
        {
            Directory.SetCurrentDirectory(origCwd);
        }
    }

    [Fact]
    public void DiscoverCommands_FindsMdFiles()
    {
        var orcaDir = Path.Combine(_tempDir, ".orca", "commands");
        Directory.CreateDirectory(orcaDir);
        File.WriteAllText(Path.Combine(orcaDir, "test-cmd.md"), "# Test Command\nDo something.");
        File.WriteAllText(Path.Combine(orcaDir, "deploy.md"), "# Deploy\nRun deploy.");
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));

        var origCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir);
        try
        {
            var loader = new CustomCommandLoader();
            var commands = loader.DiscoverCommands();

            Assert.Contains("test-cmd", commands.Keys);
            Assert.Contains("deploy", commands.Keys);
        }
        finally
        {
            Directory.SetCurrentDirectory(origCwd);
        }
    }

    [Fact]
    public async Task LoadTemplateAsync_ReturnsNullForMissing()
    {
        var origCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir);
        try
        {
            var loader = new CustomCommandLoader();
            var template = await loader.LoadTemplateAsync("nonexistent");
            Assert.Null(template);
        }
        finally
        {
            Directory.SetCurrentDirectory(origCwd);
        }
    }

    [Fact]
    public void DiscoverCommands_HandlesNonexistentDirs()
    {
        // No .git, no .orca dir
        var origCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir);
        try
        {
            var loader = new CustomCommandLoader();
            var commands = loader.DiscoverCommands();
            Assert.Empty(commands);
        }
        finally
        {
            Directory.SetCurrentDirectory(origCwd);
        }
    }
}
