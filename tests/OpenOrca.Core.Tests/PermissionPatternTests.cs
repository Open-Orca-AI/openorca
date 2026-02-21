using OpenOrca.Core.Permissions;
using Xunit;

namespace OpenOrca.Core.Tests;

public class PermissionPatternTests
{
    [Theory]
    [InlineData("Bash(git *)", "Bash", "git *")]
    [InlineData("write_file(src/**)", "write_file", "src/**")]
    [InlineData("Bash(rm -rf *)", "Bash", "rm -rf *")]
    [InlineData("Bash(sudo *)", "Bash", "sudo *")]
    public void Parse_ValidPatterns_ReturnsCorrect(string input, string expectedTool, string expectedGlob)
    {
        var result = PermissionPattern.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedTool, result!.ToolName);
        Assert.Equal(expectedGlob, result.ArgumentGlob);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("invalid")]
    [InlineData("no_parens")]
    [InlineData("(just parens)")]
    public void Parse_InvalidPatterns_ReturnsNull(string input)
    {
        var result = PermissionPattern.Parse(input);
        Assert.Null(result);
    }

    [Fact]
    public void Matches_BashGitStar_MatchesGitCommands()
    {
        var pattern = PermissionPattern.Parse("Bash(git *)");

        Assert.NotNull(pattern);
        Assert.True(pattern!.Matches("bash", "git status"));
        Assert.True(pattern.Matches("Bash", "git commit -m test"));
        Assert.True(pattern.Matches("BASH", "git push origin main"));
        Assert.False(pattern.Matches("bash", "rm -rf /"));
        Assert.False(pattern.Matches("write_file", "git status"));
    }

    [Fact]
    public void Matches_WriteFileSrcStar_MatchesSrcPaths()
    {
        var pattern = PermissionPattern.Parse("write_file(src/**)");

        Assert.NotNull(pattern);
        Assert.True(pattern!.Matches("write_file", "src/main.cs"));
        Assert.True(pattern.Matches("write_file", "src/deep/nested/file.txt"));
        Assert.False(pattern.Matches("write_file", "tests/foo.cs"));
        Assert.False(pattern.Matches("bash", "src/main.cs"));
    }

    [Fact]
    public void Matches_CaseInsensitiveToolName()
    {
        var pattern = PermissionPattern.Parse("Bash(dotnet *)");

        Assert.NotNull(pattern);
        Assert.True(pattern!.Matches("bash", "dotnet build"));
        Assert.True(pattern.Matches("BASH", "dotnet test"));
        Assert.True(pattern.Matches("Bash", "dotnet run"));
    }

    [Fact]
    public void Matches_NullRelevantArg_ReturnsFalse()
    {
        var pattern = PermissionPattern.Parse("Bash(git *)");
        Assert.False(pattern!.Matches("bash", null));
        Assert.False(pattern.Matches("bash", ""));
    }

    [Fact]
    public void ExtractRelevantArg_BashCommand()
    {
        var arg = PermissionPattern.ExtractRelevantArg("bash", """{"command": "git status"}""");
        Assert.Equal("git status", arg);
    }

    [Fact]
    public void ExtractRelevantArg_FilePath()
    {
        var arg = PermissionPattern.ExtractRelevantArg("write_file", """{"path": "src/main.cs", "content": "hello"}""");
        Assert.Equal("src/main.cs", arg);
    }

    [Fact]
    public void ExtractRelevantArg_NullJson_ReturnsNull()
    {
        Assert.Null(PermissionPattern.ExtractRelevantArg("bash", null));
        Assert.Null(PermissionPattern.ExtractRelevantArg("bash", ""));
    }

    [Fact]
    public void ExtractRelevantArg_InvalidJson_ReturnsNull()
    {
        Assert.Null(PermissionPattern.ExtractRelevantArg("bash", "not json"));
    }
}
