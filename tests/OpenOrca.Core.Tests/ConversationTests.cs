using Microsoft.Extensions.AI;
using OpenOrca.Core.Chat;
using Xunit;

namespace OpenOrca.Core.Tests;

public class ConversationTests
{
    [Fact]
    public void AddUserMessage_AddsToMessages()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Hello");

        Assert.Single(conversation.Messages);
        Assert.Equal("Hello", conversation.Messages[0].Text);
    }

    [Fact]
    public void AddAssistantMessage_AddsToMessages()
    {
        var conversation = new Conversation();
        conversation.AddAssistantMessage("Hi there");

        Assert.Single(conversation.Messages);
        Assert.Equal("Hi there", conversation.Messages[0].Text);
    }

    [Fact]
    public void AddSystemMessage_SetsSystemPrompt()
    {
        var conversation = new Conversation();
        conversation.AddSystemMessage("You are a helpful assistant.");

        Assert.Equal("You are a helpful assistant.", conversation.SystemPrompt);
    }

    [Fact]
    public void GetMessagesForApi_IncludesSystemPromptFirst()
    {
        var conversation = new Conversation();
        conversation.AddSystemMessage("System prompt");
        conversation.AddUserMessage("Hello");

        var messages = conversation.GetMessagesForApi();

        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Role.Value);
        Assert.Equal("user", messages[1].Role.Value);
    }

    [Fact]
    public void Clear_RemovesAllMessages()
    {
        var conversation = new Conversation();
        conversation.AddSystemMessage("System");
        conversation.AddUserMessage("Hello");
        conversation.AddAssistantMessage("Hi");

        conversation.Clear();

        Assert.Empty(conversation.Messages);
        Assert.Null(conversation.SystemPrompt);
    }

    [Fact]
    public void EstimateTokenCount_ReturnsReasonableEstimate()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Hello world"); // 11 chars ≈ ~3 tokens

        var estimate = conversation.EstimateTokenCount();
        Assert.True(estimate > 0);
        Assert.True(estimate < 10);
    }

    [Fact]
    public void TruncateToFit_RemovesOldMessages()
    {
        var conversation = new Conversation();
        for (var i = 0; i < 100; i++)
        {
            conversation.AddUserMessage($"Message {i} with some longer content to increase token count substantially more");
        }

        var countBefore = conversation.Messages.Count;
        conversation.TruncateToFit(50);

        Assert.True(conversation.Messages.Count < countBefore);
        Assert.True(conversation.Messages.Count >= 2); // Minimum retained
    }

    // ── CompactWithSummary tests ──

    [Fact]
    public void CompactWithSummary_ReplacesOldMessagesWithSummary()
    {
        var conversation = new Conversation();
        // Create 3 turns: user+assistant, user+assistant, user+assistant
        for (var i = 0; i < 3; i++)
        {
            conversation.AddUserMessage($"User message {i}");
            conversation.AddAssistantMessage($"Assistant message {i}");
        }

        Assert.Equal(6, conversation.Messages.Count);

        // Compact preserving last 1 turn
        var removed = conversation.CompactWithSummary("This is a summary of the conversation.", 1);

        Assert.True(removed > 0);
        // Should have: summary (user) + ack (assistant) + preserved last user + preserved last assistant
        Assert.True(conversation.Messages.Count < 6);
        // First message should be the summary
        Assert.Contains("[Conversation summary]", conversation.Messages[0].Text!);
    }

    [Fact]
    public void CompactWithSummary_PreservesLastNTurns()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Old question");
        conversation.AddAssistantMessage("Old answer");
        conversation.AddUserMessage("Recent question");
        conversation.AddAssistantMessage("Recent answer");

        conversation.CompactWithSummary("Summary of old conversation.", 1);

        // The last turn (Recent question + Recent answer) should be preserved
        var messages = conversation.Messages.ToList();
        Assert.Contains(messages, m => m.Text?.Contains("Recent question") == true);
        Assert.Contains(messages, m => m.Text?.Contains("Recent answer") == true);
    }

    [Fact]
    public void CompactWithSummary_ReturnsZero_WhenNotEnoughMessages()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Hello");

        var removed = conversation.CompactWithSummary("Summary", 5);

        Assert.Equal(0, removed);
    }

    // ── GetMessagesForCompaction tests ──

    [Fact]
    public void GetMessagesForCompaction_ReturnsOlderMessages()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Old 1");
        conversation.AddAssistantMessage("Old response 1");
        conversation.AddUserMessage("Recent 1");
        conversation.AddAssistantMessage("Recent response 1");

        var toCompact = conversation.GetMessagesForCompaction(1);

        // Should return the first 2 messages (old turn)
        Assert.Equal(2, toCompact.Count);
        Assert.Equal("Old 1", toCompact[0].Text);
    }

    [Fact]
    public void GetMessagesForCompaction_ReturnsEmpty_WhenPreserveExceedsCount()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Hello");

        var toCompact = conversation.GetMessagesForCompaction(5);

        Assert.Empty(toCompact);
    }

    // ── RemoveLastTurns tests ──

    [Fact]
    public void RemoveLastTurns_RemovesOneTurn()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("First question");
        conversation.AddAssistantMessage("First answer");
        conversation.AddUserMessage("Second question");
        conversation.AddAssistantMessage("Second answer");

        var removed = conversation.RemoveLastTurns(1);

        Assert.Equal(2, removed); // user + assistant
        Assert.Equal(2, conversation.Messages.Count);
        Assert.Equal("First question", conversation.Messages[0].Text);
    }

    [Fact]
    public void RemoveLastTurns_RemovesMultipleTurns()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Q1");
        conversation.AddAssistantMessage("A1");
        conversation.AddUserMessage("Q2");
        conversation.AddAssistantMessage("A2");
        conversation.AddUserMessage("Q3");
        conversation.AddAssistantMessage("A3");

        var removed = conversation.RemoveLastTurns(2);

        Assert.Equal(4, removed);
        Assert.Equal(2, conversation.Messages.Count);
        Assert.Equal("Q1", conversation.Messages[0].Text);
    }

    [Fact]
    public void RemoveLastTurns_HandlesToolMessages()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Do something");
        conversation.AddAssistantMessage("Using a tool");
        conversation.AddMessage(new ChatMessage(ChatRole.Tool, "tool result"));
        conversation.AddAssistantMessage("Done!");

        var removed = conversation.RemoveLastTurns(1);

        // Should remove: assistant "Done!", tool "tool result", assistant "Using a tool", user "Do something"
        Assert.Equal(4, removed);
        Assert.Empty(conversation.Messages);
    }

    [Fact]
    public void RemoveLastTurns_ReturnsZero_WhenEmpty()
    {
        var conversation = new Conversation();
        var removed = conversation.RemoveLastTurns(1);

        Assert.Equal(0, removed);
    }

    // ── GetMessageCountByRole tests ──

    [Fact]
    public void GetMessageCountByRole_CountsCorrectly()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Q1");
        conversation.AddAssistantMessage("A1");
        conversation.AddUserMessage("Q2");
        conversation.AddAssistantMessage("A2");
        conversation.AddMessage(new ChatMessage(ChatRole.Tool, "result"));

        var counts = conversation.GetMessageCountByRole();

        Assert.Equal(2, counts["user"]);
        Assert.Equal(2, counts["assistant"]);
        Assert.Equal(1, counts["tool"]);
    }

    [Fact]
    public void GetMessageCountByRole_ReturnsEmpty_WhenNoMessages()
    {
        var conversation = new Conversation();
        var counts = conversation.GetMessageCountByRole();

        Assert.Empty(counts);
    }
}
