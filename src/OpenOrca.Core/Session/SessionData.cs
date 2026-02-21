using System.Text.Json.Serialization;

namespace OpenOrca.Core.Session;

public sealed class SessionData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "Untitled";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? WorkingDirectory { get; set; }
    public string? Model { get; set; }
    public string? SystemPrompt { get; set; }
    public List<SessionMessage> Messages { get; set; } = [];
    public string? ParentSessionId { get; set; }
    public int? ForkPointMessageIndex { get; set; }
}

public sealed class SessionMessage
{
    public string Role { get; set; } = "";
    public string? Text { get; set; }
    public List<SessionToolCall>? ToolCalls { get; set; }
    public List<SessionToolResult>? ToolResults { get; set; }
}

public sealed class SessionToolCall
{
    public string? CallId { get; set; }
    public string Name { get; set; } = "";
    public string? Arguments { get; set; }
}

public sealed class SessionToolResult
{
    public string? CallId { get; set; }
    public string Result { get; set; } = "";
}
