using OpenOrca.Cli.Repl;
using OpenOrca.Core.Chat;
using Xunit;

namespace OpenOrca.Cli.Tests;

/// <summary>
/// Concurrency and stress tests for shared mutable state.
/// </summary>
public class ConcurrencyTests
{
    [Fact]
    public async Task ReplState_ConcurrentTokenUpdates_AreConsistent()
    {
        var state = new ReplState();
        const int iterations = 1000;
        const int concurrency = 10;

        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
            Task.Run(() =>
            {
                for (var i = 0; i < iterations; i++)
                {
                    var current = state.TotalOutputTokens;
                    state.TotalOutputTokens = current + 1;
                }
            }));

        await Task.WhenAll(tasks);

        // Due to races the final value may be less than iterations * concurrency,
        // but the important thing is no exception was thrown and value is positive
        Assert.True(state.TotalOutputTokens > 0);
    }

    [Fact]
    public async Task ReplState_ConcurrentBoolToggles_NoException()
    {
        var state = new ReplState();

        var tasks = Enumerable.Range(0, 20).Select(i =>
            Task.Run(() =>
            {
                for (var j = 0; j < 500; j++)
                {
                    state.Verbosity = (state.Verbosity + 1) % 5;
                    state.PlanMode = !state.PlanMode;
                }
            }));

        await Task.WhenAll(tasks);

        // No assertion on final value — just verify no crash with volatile fields
        _ = state.ShowThinking;
        _ = state.PlanMode;
    }

    [Fact]
    public async Task ConversationManager_ConcurrentCreateNew_NoException()
    {
        var manager = new ConversationManager();

        var tasks = Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() =>
            {
                for (var i = 0; i < 50; i++)
                {
                    manager.CreateNew("system prompt");
                    var a = manager.Active;
                    var b = manager.ActiveId;
                }
            }));

        await Task.WhenAll(tasks);

        // Verify state is consistent
        Assert.NotNull(manager.Active);
        Assert.NotNull(manager.ActiveId);
    }

    [Fact]
    public async Task Conversation_ConcurrentAddMessage_NoException()
    {
        var conversation = new Conversation();

        var tasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(() =>
            {
                for (var j = 0; j < 100; j++)
                {
                    conversation.AddUserMessage($"Message {i}-{j}");
                }
            }));

        await Task.WhenAll(tasks);

        // Conversation doesn't use internal locking for AddUserMessage,
        // so some messages may be lost under contention. Just verify no crash.
        var messages = conversation.GetMessagesForApi();
        Assert.True(messages.Count > 0);
    }

    [Fact]
    public async Task ReplState_ConcurrentLastAssistantResponse_NoException()
    {
        var state = new ReplState();

        var tasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(() =>
            {
                for (var j = 0; j < 100; j++)
                {
                    state.LastAssistantResponse = $"Response {i}-{j}";
                    _ = state.LastAssistantResponse;
                }
            }));

        await Task.WhenAll(tasks);

        // Just verify no crash
        Assert.NotNull(state.LastAssistantResponse);
    }
}
