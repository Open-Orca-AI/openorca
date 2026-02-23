namespace OpenOrca.Core.Chat;

public sealed class ConversationManager
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Conversation> _conversations = [];

    private volatile Conversation _active = new();
    private volatile string _activeId = Guid.NewGuid().ToString("N")[..8];

    public Conversation Active => _active;
    public string ActiveId => _activeId;

    public Conversation CreateNew(string? systemPrompt = null)
    {
        lock (_lock)
        {
            _active = new Conversation();
            _activeId = Guid.NewGuid().ToString("N")[..8];

            if (systemPrompt is not null)
                _active.AddSystemMessage(systemPrompt);

            _conversations[_activeId] = _active;
            return _active;
        }
    }

    public void SetActive(string id, Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        lock (_lock)
        {
            _activeId = id;
            _active = conversation;
            _conversations[id] = conversation;
        }
    }

    public IReadOnlyDictionary<string, Conversation> GetAll()
    {
        lock (_lock)
        {
            return new Dictionary<string, Conversation>(_conversations);
        }
    }
}
