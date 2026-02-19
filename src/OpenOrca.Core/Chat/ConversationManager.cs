namespace OpenOrca.Core.Chat;

public sealed class ConversationManager
{
    private readonly Dictionary<string, Conversation> _conversations = [];

    public Conversation Active { get; private set; } = new();
    public string ActiveId { get; private set; } = Guid.NewGuid().ToString("N")[..8];

    public Conversation CreateNew(string? systemPrompt = null)
    {
        Active = new Conversation();
        ActiveId = Guid.NewGuid().ToString("N")[..8];

        if (systemPrompt is not null)
            Active.AddSystemMessage(systemPrompt);

        _conversations[ActiveId] = Active;
        return Active;
    }

    public void SetActive(string id, Conversation conversation)
    {
        ActiveId = id;
        Active = conversation;
        _conversations[id] = conversation;
    }

    public IReadOnlyDictionary<string, Conversation> GetAll() => _conversations;
}
