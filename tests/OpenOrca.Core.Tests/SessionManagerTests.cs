using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Session;
using Xunit;

namespace OpenOrca.Core.Tests;

public class SessionManagerTests : IDisposable
{
    private readonly SessionManager _manager;
    private readonly string _sessionDir;

    public SessionManagerTests()
    {
        var config = new OrcaConfig();
        _manager = new SessionManager(config, NullLogger<SessionManager>.Instance);

        // Resolve the actual session dir so we can clean up test files
        _sessionDir = Path.Combine(ConfigManager.GetConfigDirectory(), "sessions");
        Directory.CreateDirectory(_sessionDir);
    }

    public void Dispose()
    {
        // Clean up any test session files we created (by prefix)
        if (Directory.Exists(_sessionDir))
        {
            foreach (var file in Directory.GetFiles(_sessionDir, "test_*.json"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    // ── Save and reload: text messages only ──

    [Fact]
    public async Task SaveAndLoad_TextMessagesOnly_RoundTrips()
    {
        var conversation = new Conversation();
        conversation.AddSystemMessage("You are a helpful assistant.");
        conversation.AddUserMessage("Hello");
        conversation.AddAssistantMessage("Hi there!");

        var id = await _manager.SaveAsync(conversation, "Test session", $"test_{Guid.NewGuid():N}");

        var loaded = await _manager.LoadAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("Test session", loaded.Title);
        Assert.Equal("You are a helpful assistant.", loaded.SystemPrompt);
        Assert.Equal(2, loaded.Messages.Count); // user + assistant (system is separate)
        Assert.Equal("user", loaded.Messages[0].Role);
        Assert.Equal("Hello", loaded.Messages[0].Text);
        Assert.Equal("assistant", loaded.Messages[1].Role);
        Assert.Equal("Hi there!", loaded.Messages[1].Text);

        // Clean up
        _manager.Delete(id);
    }

    // ── Save and reload: with tool calls ──

    [Fact]
    public async Task SaveAndLoad_WithToolCalls_RoundTrips()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Read test.txt");

        var assistantMsg = new ChatMessage(ChatRole.Assistant, "");
        assistantMsg.Contents.Add(new TextContent("I'll read that file."));
        assistantMsg.Contents.Add(new FunctionCallContent(
            "call_123", "read_file",
            new Dictionary<string, object?> { ["path"] = "test.txt" }));
        conversation.AddMessage(assistantMsg);

        var id = await _manager.SaveAsync(conversation, "Tool test", $"test_{Guid.NewGuid():N}");
        var loaded = await _manager.LoadAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Messages.Count);

        var savedAssistant = loaded.Messages[1];
        Assert.Equal("assistant", savedAssistant.Role);
        Assert.NotNull(savedAssistant.ToolCalls);
        Assert.Single(savedAssistant.ToolCalls);
        Assert.Equal("call_123", savedAssistant.ToolCalls[0].CallId);
        Assert.Equal("read_file", savedAssistant.ToolCalls[0].Name);
        Assert.Contains("test.txt", savedAssistant.ToolCalls[0].Arguments!);

        _manager.Delete(id);
    }

    // ── Save and reload: with tool results ──

    [Fact]
    public async Task SaveAndLoad_WithToolResults_RoundTrips()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Read it");

        var toolMsg = new ChatMessage(ChatRole.Tool, "");
        toolMsg.Contents.Add(new FunctionResultContent("call_456", "file contents here"));
        conversation.AddMessage(toolMsg);

        var id = await _manager.SaveAsync(conversation, "Result test", $"test_{Guid.NewGuid():N}");
        var loaded = await _manager.LoadAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Messages.Count);

        var savedTool = loaded.Messages[1];
        Assert.Equal("tool", savedTool.Role);
        Assert.NotNull(savedTool.ToolResults);
        Assert.Single(savedTool.ToolResults);
        Assert.Equal("call_456", savedTool.ToolResults[0].CallId);
        Assert.Equal("file contents here", savedTool.ToolResults[0].Result);

        _manager.Delete(id);
    }

    // ── Round-trip preserves ordering ──

    [Fact]
    public async Task SaveAndLoad_PreservesMessageOrdering()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("First");
        conversation.AddAssistantMessage("Reply 1");
        conversation.AddUserMessage("Second");
        conversation.AddAssistantMessage("Reply 2");
        conversation.AddUserMessage("Third");

        var id = await _manager.SaveAsync(conversation, "Order test", $"test_{Guid.NewGuid():N}");
        var loaded = await _manager.LoadAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal(5, loaded.Messages.Count);
        Assert.Equal("First", loaded.Messages[0].Text);
        Assert.Equal("Reply 1", loaded.Messages[1].Text);
        Assert.Equal("Second", loaded.Messages[2].Text);
        Assert.Equal("Reply 2", loaded.Messages[3].Text);
        Assert.Equal("Third", loaded.Messages[4].Text);

        _manager.Delete(id);
    }

    // ── SessionToConversation round-trip ──

    [Fact]
    public async Task SessionToConversation_RestoresConversationCorrectly()
    {
        var original = new Conversation();
        original.AddSystemMessage("System prompt");
        original.AddUserMessage("Hello");
        original.AddAssistantMessage("Hi!");

        var id = await _manager.SaveAsync(original, "Restore test", $"test_{Guid.NewGuid():N}");
        var loaded = await _manager.LoadAsync(id);
        Assert.NotNull(loaded);

        var restored = _manager.SessionToConversation(loaded);

        Assert.Equal("System prompt", restored.SystemPrompt);
        Assert.Equal(2, restored.Messages.Count);
        Assert.Equal(ChatRole.User, restored.Messages[0].Role);
        Assert.Equal(ChatRole.Assistant, restored.Messages[1].Role);

        _manager.Delete(id);
    }

    // ── SessionToConversation with tool calls ──

    [Fact]
    public async Task SessionToConversation_RestoresToolCalls()
    {
        var original = new Conversation();
        original.AddUserMessage("Do it");

        var msg = new ChatMessage(ChatRole.Assistant, "");
        msg.Contents.Add(new FunctionCallContent("c1", "bash",
            new Dictionary<string, object?> { ["command"] = "ls" }));
        original.AddMessage(msg);

        var toolResult = new ChatMessage(ChatRole.Tool, "");
        toolResult.Contents.Add(new FunctionResultContent("c1", "file1.txt\nfile2.txt"));
        original.AddMessage(toolResult);

        var id = await _manager.SaveAsync(original, "TC restore", $"test_{Guid.NewGuid():N}");
        var loaded = await _manager.LoadAsync(id);
        Assert.NotNull(loaded);

        var restored = _manager.SessionToConversation(loaded);

        Assert.Equal(3, restored.Messages.Count);

        // Check tool call
        var assistantContents = restored.Messages[1].Contents;
        var fc = assistantContents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(fc);
        Assert.Equal("bash", fc.Name);

        // Check tool result
        var toolContents = restored.Messages[2].Contents;
        var fr = toolContents.OfType<FunctionResultContent>().FirstOrDefault();
        Assert.NotNull(fr);
        Assert.Contains("file1.txt", fr.Result?.ToString());

        _manager.Delete(id);
    }

    // ── Load non-existent returns null ──

    [Fact]
    public async Task Load_NonExistentId_ReturnsNull()
    {
        var result = await _manager.LoadAsync("nonexistent_id_12345");
        Assert.Null(result);
    }

    // ── Delete returns false for non-existent ──

    [Fact]
    public void Delete_NonExistentSession_ReturnsFalse()
    {
        Assert.False(_manager.Delete("nonexistent_delete_test"));
    }

    // ── Delete existing returns true ──

    [Fact]
    public async Task Delete_ExistingSession_ReturnsTrue()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Temp");

        var id = await _manager.SaveAsync(conversation, "Delete test", $"test_{Guid.NewGuid():N}");
        Assert.True(_manager.Delete(id));

        // Verify it's gone
        var loaded = await _manager.LoadAsync(id);
        Assert.Null(loaded);
    }

    // ── List returns sessions ──

    [Fact]
    public async Task List_ReturnsSavedSessions()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("List test");

        var id = await _manager.SaveAsync(conversation, "List test session", $"test_{Guid.NewGuid():N}");

        var sessions = _manager.List();
        Assert.Contains(sessions, s => s.Id == id);

        _manager.Delete(id);
    }

    // ── GenerateTitle uses first user message ──

    [Fact]
    public async Task SaveWithoutTitle_UsesFirstUserMessage()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Help me write a function");

        var id = await _manager.SaveAsync(conversation, existingId: $"test_{Guid.NewGuid():N}");
        var loaded = await _manager.LoadAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("Help me write a function", loaded.Title);

        _manager.Delete(id);
    }

    // ── GenerateTitle truncation ──

    [Fact]
    public async Task SaveWithoutTitle_LongMessage_Truncates()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage(new string('A', 100));

        var id = await _manager.SaveAsync(conversation, existingId: $"test_{Guid.NewGuid():N}");
        var loaded = await _manager.LoadAsync(id);

        Assert.NotNull(loaded);
        Assert.True(loaded.Title.Length <= 60);
        Assert.EndsWith("...", loaded.Title);

        _manager.Delete(id);
    }

    // ── Tool call with null arguments ──

    [Fact]
    public async Task SaveAndLoad_ToolCallNullArguments_NoError()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Test");

        var msg = new ChatMessage(ChatRole.Assistant, "");
        msg.Contents.Add(new FunctionCallContent("c_null", "some_tool", null));
        conversation.AddMessage(msg);

        var id = await _manager.SaveAsync(conversation, "Null args", $"test_{Guid.NewGuid():N}");
        var loaded = await _manager.LoadAsync(id);

        Assert.NotNull(loaded);
        var tc = loaded.Messages[1].ToolCalls;
        Assert.NotNull(tc);
        Assert.Single(tc);
        Assert.Equal("some_tool", tc[0].Name);
        Assert.Null(tc[0].Arguments);

        _manager.Delete(id);
    }
}
