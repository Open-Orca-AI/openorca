using OpenOrca.Core.Chat;
using Xunit;

namespace OpenOrca.Core.Tests;

public class ConversationManagerTests
{
    [Fact]
    public void CreateNew_ReturnsNewConversation()
    {
        var manager = new ConversationManager();
        var conversation = manager.CreateNew("Test system prompt");

        Assert.NotNull(conversation);
        Assert.Equal("Test system prompt", conversation.SystemPrompt);
        Assert.Same(conversation, manager.Active);
    }

    [Fact]
    public void CreateNew_GeneratesUniqueId()
    {
        var manager = new ConversationManager();
        var id1 = manager.ActiveId;

        manager.CreateNew();
        var id2 = manager.ActiveId;

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void SetActive_UpdatesActiveConversation()
    {
        var manager = new ConversationManager();
        var newConvo = new Conversation();
        newConvo.AddUserMessage("Hello");

        manager.SetActive("test-id", newConvo);

        Assert.Same(newConvo, manager.Active);
        Assert.Equal("test-id", manager.ActiveId);
    }
}
