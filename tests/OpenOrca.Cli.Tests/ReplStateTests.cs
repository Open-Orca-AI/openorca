using OpenOrca.Cli.Repl;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class ReplStateTests
{
    [Fact]
    public void DefaultState_ShowThinkingIsFalse()
    {
        var state = new ReplState();
        Assert.False(state.ShowThinking);
    }

    [Fact]
    public void DefaultState_PlanModeIsFalse()
    {
        var state = new ReplState();
        Assert.False(state.PlanMode);
    }

    [Fact]
    public void DefaultState_TokensAndTurnsAreZero()
    {
        var state = new ReplState();
        Assert.Equal(0, state.TotalOutputTokens);
        Assert.Equal(0, state.TotalTurns);
    }

    [Fact]
    public void ShowThinking_CanBeToggled()
    {
        var state = new ReplState();
        state.ShowThinking = true;
        Assert.True(state.ShowThinking);
        state.ShowThinking = false;
        Assert.False(state.ShowThinking);
    }

    [Fact]
    public void PlanMode_CanBeToggled()
    {
        var state = new ReplState();
        state.PlanMode = true;
        Assert.True(state.PlanMode);
        state.PlanMode = false;
        Assert.False(state.PlanMode);
    }

    [Fact]
    public void TotalOutputTokens_Accumulates()
    {
        var state = new ReplState();
        state.TotalOutputTokens = 100;
        Assert.Equal(100, state.TotalOutputTokens);
        state.TotalOutputTokens = 250;
        Assert.Equal(250, state.TotalOutputTokens);
    }

    [Fact]
    public void TotalTurns_Accumulates()
    {
        var state = new ReplState();
        state.TotalTurns = 1;
        Assert.Equal(1, state.TotalTurns);
        state.TotalTurns = 5;
        Assert.Equal(5, state.TotalTurns);
    }

    [Fact]
    public void SessionStopwatch_IsRunning()
    {
        var state = new ReplState();
        Assert.True(state.SessionStopwatch.IsRunning);
    }

    [Fact]
    public void LastAssistantResponse_DefaultIsNull()
    {
        var state = new ReplState();
        Assert.Null(state.LastAssistantResponse);
    }

    [Fact]
    public void LastAssistantResponse_CanBeSet()
    {
        var state = new ReplState();
        state.LastAssistantResponse = "Hello world";
        Assert.Equal("Hello world", state.LastAssistantResponse);
    }

    [Fact]
    public void CurrentSessionId_DefaultIsNull()
    {
        var state = new ReplState();
        Assert.Null(state.CurrentSessionId);
    }

    [Fact]
    public void DefaultState_ModeIsNormal()
    {
        var state = new ReplState();
        Assert.Equal(InputMode.Normal, state.Mode);
    }

    [Fact]
    public void CycleMode_NormalToPlanToAskToNormal()
    {
        var state = new ReplState();
        Assert.Equal(InputMode.Normal, state.Mode);

        state.CycleMode();
        Assert.Equal(InputMode.Plan, state.Mode);

        state.CycleMode();
        Assert.Equal(InputMode.Ask, state.Mode);

        state.CycleMode();
        Assert.Equal(InputMode.Normal, state.Mode);
    }

    [Fact]
    public void PlanMode_SetTrue_SetsModeToplan()
    {
        var state = new ReplState();
        state.PlanMode = true;
        Assert.Equal(InputMode.Plan, state.Mode);
        Assert.True(state.PlanMode);
    }

    [Fact]
    public void PlanMode_SetFalse_SetsModeToNormal()
    {
        var state = new ReplState();
        state.Mode = InputMode.Plan;
        state.PlanMode = false;
        Assert.Equal(InputMode.Normal, state.Mode);
        Assert.False(state.PlanMode);
    }

    [Fact]
    public void PlanMode_IsFalseInAskMode()
    {
        var state = new ReplState();
        state.Mode = InputMode.Ask;
        Assert.False(state.PlanMode);
    }

    [Fact]
    public void Mode_SetDirectly_ReflectsInPlanMode()
    {
        var state = new ReplState();

        state.Mode = InputMode.Plan;
        Assert.True(state.PlanMode);

        state.Mode = InputMode.Ask;
        Assert.False(state.PlanMode);

        state.Mode = InputMode.Normal;
        Assert.False(state.PlanMode);
    }
}
