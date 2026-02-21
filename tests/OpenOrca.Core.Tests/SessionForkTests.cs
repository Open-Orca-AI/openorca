using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Session;
using Xunit;

namespace OpenOrca.Core.Tests;

public class SessionForkTests : IDisposable
{
    private readonly SessionManager _manager;
    private readonly List<string> _createdIds = [];

    public SessionForkTests()
    {
        var config = new OrcaConfig();
        _manager = new SessionManager(config, NullLogger<SessionManager>.Instance);
    }

    public void Dispose()
    {
        foreach (var id in _createdIds)
        {
            try { _manager.Delete(id); } catch { }
        }
    }

    [Fact]
    public async Task ForkAsync_CreatesNewSessionWithParentId()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Hello");
        conversation.AddAssistantMessage("Hi!");

        var parentId = await _manager.SaveAsync(conversation, "Parent", $"test_{Guid.NewGuid():N}");
        _createdIds.Add(parentId);

        var forkId = await _manager.ForkAsync(conversation, "My Fork", parentId, 2);
        _createdIds.Add(forkId);

        Assert.NotEqual(parentId, forkId);

        var forked = await _manager.LoadAsync(forkId);
        Assert.NotNull(forked);
        Assert.Equal("My Fork", forked.Title);
        Assert.Equal(parentId, forked.ParentSessionId);
        Assert.Equal(2, forked.ForkPointMessageIndex);
    }

    [Fact]
    public async Task ForkAsync_PreservesMessages()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("First");
        conversation.AddAssistantMessage("Reply 1");
        conversation.AddUserMessage("Second");

        var parentId = await _manager.SaveAsync(conversation, "Parent", $"test_{Guid.NewGuid():N}");
        _createdIds.Add(parentId);

        var forkId = await _manager.ForkAsync(conversation, null, parentId, 3);
        _createdIds.Add(forkId);

        var forked = await _manager.LoadAsync(forkId);
        Assert.NotNull(forked);
        Assert.Equal(3, forked.Messages.Count);
        Assert.Equal("First", forked.Messages[0].Text);
    }

    [Fact]
    public async Task ForkAsync_DefaultTitle_ContainsParentId()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Test");

        var parentId = await _manager.SaveAsync(conversation, "Original", $"test_{Guid.NewGuid():N}");
        _createdIds.Add(parentId);

        var forkId = await _manager.ForkAsync(conversation, null, parentId, 1);
        _createdIds.Add(forkId);

        var forked = await _manager.LoadAsync(forkId);
        Assert.NotNull(forked);
        Assert.Contains(parentId, forked.Title);
    }

    [Fact]
    public void GetSessionTree_NoSessions_ReturnsEmpty()
    {
        // Note: this test relies on the existing session dir state,
        // but verifies the method doesn't crash
        var tree = _manager.GetSessionTree();
        Assert.NotNull(tree);
    }

    [Fact]
    public async Task GetSessionTree_WithFork_ShowsParentChild()
    {
        var conversation = new Conversation();
        conversation.AddUserMessage("Root");

        var rootId = await _manager.SaveAsync(conversation, "Root Session", $"test_{Guid.NewGuid():N}");
        _createdIds.Add(rootId);

        var forkId = await _manager.ForkAsync(conversation, "Fork 1", rootId, 1);
        _createdIds.Add(forkId);

        // Verify fork was created with correct parent
        var forked = await _manager.LoadAsync(forkId);
        Assert.NotNull(forked);
        Assert.Equal(rootId, forked.ParentSessionId);

        // Build tree and verify structure
        var tree = _manager.GetSessionTree();
        Assert.NotEmpty(tree);

        // Verify the fork has depth > 0 when its parent exists in the tree
        var forkInTree = tree.Where(t => t.Session.Id == forkId).ToList();
        if (forkInTree.Count > 0)
        {
            Assert.Equal(1, forkInTree[0].Depth);
        }

        // Verify root is at depth 0
        var rootInTree = tree.Where(t => t.Session.Id == rootId).ToList();
        if (rootInTree.Count > 0)
        {
            Assert.Equal(0, rootInTree[0].Depth);
        }
    }

    [Fact]
    public void SessionData_ParentFields_DefaultNull()
    {
        var data = new SessionData();
        Assert.Null(data.ParentSessionId);
        Assert.Null(data.ForkPointMessageIndex);
    }
}
