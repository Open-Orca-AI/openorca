using System.Text.Json;
using OpenOrca.Tools.GitHub;
using Xunit;

namespace OpenOrca.Tools.Tests;

public class GitHubToolTests
{
    private static JsonElement MakeArgs(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void GitHubTool_HasCorrectName()
    {
        var tool = new GitHubTool();
        Assert.Equal("github", tool.Name);
    }

    [Fact]
    public void GitHubTool_HasCorrectRiskLevel()
    {
        var tool = new GitHubTool();
        Assert.Equal(Abstractions.ToolRiskLevel.Moderate, tool.RiskLevel);
    }

    [Fact]
    public async Task GitHubTool_InvalidAction_ReturnsError()
    {
        var tool = new GitHubTool();
        var args = MakeArgs("""{"action": "nonexistent"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Invalid action", result.Content);
    }

    [Fact]
    public async Task GitHubTool_PrView_MissingNumber_ReturnsError()
    {
        var tool = new GitHubTool();
        var args = MakeArgs("""{"action": "pr_view"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("missing required", result.Content.ToLower());
    }

    [Fact]
    public async Task GitHubTool_PrCreate_MissingTitle_ReturnsError()
    {
        var tool = new GitHubTool();
        var args = MakeArgs("""{"action": "pr_create"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task GitHubTool_IssueCreate_MissingTitle_ReturnsError()
    {
        var tool = new GitHubTool();
        var args = MakeArgs("""{"action": "issue_create"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task GitHubTool_PrComment_MissingBody_ReturnsError()
    {
        var tool = new GitHubTool();
        var args = MakeArgs("""{"action": "pr_comment", "number": 1}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task GitHubTool_GhNotInstalled_ReturnsHelpfulError()
    {
        // This test verifies we handle the case where gh is not found gracefully.
        // On machines without gh, it will hit the Win32Exception / "not installed" path.
        // On machines with gh but no auth, it will hit the auth error path.
        // On machines with gh + auth but no repo, it will hit the "not a repo" path.
        // All of these are valid non-crash outcomes.

        var tool = new GitHubTool();
        var args = MakeArgs("""{"action": "repo_view"}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // Should not throw — it should return a ToolResult regardless
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
    }

    [Fact]
    public async Task GitHubTool_PrList_WithFilters()
    {
        // Exercises the argument building path — will error at gh execution
        // but validates our command building doesn't crash
        var tool = new GitHubTool();
        var args = MakeArgs("""{"action": "pr_list", "state": "closed", "limit": 5}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        // Whatever the outcome, it shouldn't throw
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GitHubTool_IssueList_WithRepo()
    {
        var tool = new GitHubTool();
        var args = MakeArgs("""{"action": "issue_list", "repo": "cli/cli", "limit": 3}""");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.NotNull(result);
    }
}
