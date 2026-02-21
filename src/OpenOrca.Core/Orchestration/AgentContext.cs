using OpenOrca.Core.Chat;

namespace OpenOrca.Core.Orchestration;

public enum AgentStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class AgentContext
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Task { get; init; } = "";
    public string AgentType { get; init; } = "general";
    public Conversation Conversation { get; } = new();
    public AgentStatus Status { get; set; } = AgentStatus.Pending;
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int IterationCount { get; set; }
}
