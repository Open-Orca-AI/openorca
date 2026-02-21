using OpenOrca.Cli.Rendering;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class ThinkingIndicatorTests
{
    [Fact]
    public void BudgetTokens_DefaultsToZero()
    {
        using var indicator = new ThinkingIndicator(TextWriter.Null);
        Assert.Equal(0, indicator.BudgetTokens);
    }

    [Fact]
    public void BudgetTokens_CanBeSet()
    {
        using var indicator = new ThinkingIndicator(TextWriter.Null);
        indicator.BudgetTokens = 200;
        Assert.Equal(200, indicator.BudgetTokens);
    }

    [Fact]
    public void Stop_CanBeCalledMultipleTimes()
    {
        using var indicator = new ThinkingIndicator(TextWriter.Null);
        indicator.Stop();
        indicator.Stop(); // Should not throw
    }

    [Fact]
    public void UpdateTokenCount_SetsReceivingTokens()
    {
        using var indicator = new ThinkingIndicator(TextWriter.Null);
        indicator.UpdateTokenCount(42);
        // No assertion on internal state, just verify no crash
        indicator.Stop();
    }
}
