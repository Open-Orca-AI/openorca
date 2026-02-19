using Microsoft.Extensions.AI;

namespace OpenOrca.Core.Chat;

public sealed class Conversation
{
    private readonly List<ChatMessage> _messages = [];

    public string? SystemPrompt { get; set; }

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public void AddSystemMessage(string content)
    {
        SystemPrompt = content;
    }

    public void AddUserMessage(string content)
    {
        _messages.Add(new ChatMessage(ChatRole.User, content));
    }

    public void AddAssistantMessage(string content)
    {
        _messages.Add(new ChatMessage(ChatRole.Assistant, content));
    }

    public void AddMessage(ChatMessage message)
    {
        _messages.Add(message);
    }

    public void AddMessages(IEnumerable<ChatMessage> messages)
    {
        _messages.AddRange(messages);
    }

    public List<ChatMessage> GetMessagesForApi()
    {
        var result = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(SystemPrompt))
        {
            result.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        }

        result.AddRange(_messages);
        return result;
    }

    public void Clear()
    {
        _messages.Clear();
        SystemPrompt = null;
    }

    public int EstimateTokenCount()
    {
        // Rough estimation: ~4 chars per token
        var totalChars = (SystemPrompt?.Length ?? 0) +
            _messages.Sum(m => m.Text?.Length ?? 0);
        return totalChars / 4;
    }

    public void TruncateToFit(int maxTokens)
    {
        while (_messages.Count > 2 && EstimateTokenCount() > maxTokens)
        {
            _messages.RemoveAt(0);
        }
    }

    /// <summary>
    /// Get messages that would be summarized during compaction (all except the last N user turns).
    /// </summary>
    public List<ChatMessage> GetMessagesForCompaction(int preserveLastN)
    {
        if (preserveLastN >= _messages.Count)
            return [];

        // Find the index where preserved messages start (last N user messages and their responses)
        var preserveStartIndex = FindPreserveStartIndex(preserveLastN);
        return _messages.Take(preserveStartIndex).ToList();
    }

    /// <summary>
    /// Replace old messages with a summary message, preserving the last N turns.
    /// Returns the number of messages removed.
    /// </summary>
    public int CompactWithSummary(string summary, int preserveLastN)
    {
        var preserveStartIndex = FindPreserveStartIndex(preserveLastN);
        if (preserveStartIndex <= 0)
            return 0;

        var removedCount = preserveStartIndex;
        _messages.RemoveRange(0, preserveStartIndex);

        // Insert summary as the first message
        _messages.Insert(0, new ChatMessage(ChatRole.User, "[Conversation summary]\n" + summary));
        _messages.Insert(1, new ChatMessage(ChatRole.Assistant, "Understood. I have the context from the conversation summary above. How can I help?"));

        return removedCount;
    }

    /// <summary>
    /// Remove the last N conversation turns. A turn = user message + all subsequent non-user messages.
    /// Returns the total number of messages removed.
    /// </summary>
    public int RemoveLastTurns(int turnCount)
    {
        var removed = 0;

        for (var t = 0; t < turnCount && _messages.Count > 0; t++)
        {
            // Remove trailing non-user messages (assistant/tool responses)
            while (_messages.Count > 0 && _messages[^1].Role != ChatRole.User)
            {
                _messages.RemoveAt(_messages.Count - 1);
                removed++;
            }

            // Remove the user message
            if (_messages.Count > 0 && _messages[^1].Role == ChatRole.User)
            {
                _messages.RemoveAt(_messages.Count - 1);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Count messages grouped by role.
    /// </summary>
    public Dictionary<string, int> GetMessageCountByRole()
    {
        var counts = new Dictionary<string, int>();
        foreach (var msg in _messages)
        {
            var role = msg.Role.Value;
            counts[role] = counts.GetValueOrDefault(role) + 1;
        }
        return counts;
    }

    private int FindPreserveStartIndex(int preserveLastN)
    {
        if (preserveLastN <= 0)
            return _messages.Count;

        // Walk backwards counting user messages to find where preserved section starts
        var userCount = 0;
        for (var i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i].Role == ChatRole.User)
            {
                userCount++;
                if (userCount >= preserveLastN)
                    return i;
            }
        }

        return 0; // Not enough user messages; preserve everything
    }
}
