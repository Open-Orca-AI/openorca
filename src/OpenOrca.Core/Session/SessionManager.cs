using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;

namespace OpenOrca.Core.Session;

public sealed class SessionManager
{
    private static readonly string SessionDir = Path.Combine(
        ConfigManager.GetConfigDirectory(), "sessions");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OrcaConfig _config;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(OrcaConfig config, ILogger<SessionManager> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<string> SaveAsync(Conversation conversation, string? title = null, string? existingId = null)
    {
        Directory.CreateDirectory(SessionDir);

        var session = ConversationToSession(conversation);
        session.Id = existingId ?? session.Id;
        session.Title = title ?? GenerateTitle(conversation);
        session.WorkingDirectory = Directory.GetCurrentDirectory();

        var path = GetSessionPath(session.Id);
        var json = JsonSerializer.Serialize(session, JsonOptions);
        await File.WriteAllTextAsync(path, json);

        _logger.LogInformation("Session saved: {Id} ({Title})", session.Id, session.Title);
        return session.Id;
    }

    public async Task<SessionData?> LoadAsync(string id)
    {
        var path = GetSessionPath(id);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<SessionData>(json, JsonOptions);
    }

    public Conversation SessionToConversation(SessionData session)
    {
        var conversation = new Conversation();

        if (!string.IsNullOrEmpty(session.SystemPrompt))
            conversation.AddSystemMessage(session.SystemPrompt);

        foreach (var msg in session.Messages)
        {
            var role = msg.Role.ToLowerInvariant() switch
            {
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                "tool" => ChatRole.Tool,
                "system" => ChatRole.System,
                _ => ChatRole.User
            };

            var chatMessage = new ChatMessage(role, msg.Text ?? "");

            // Restore tool calls
            if (msg.ToolCalls is not null)
            {
                foreach (var tc in msg.ToolCalls)
                {
                    IDictionary<string, object?>? args = null;
                    if (tc.Arguments is not null)
                    {
                        try
                        {
                            args = JsonSerializer.Deserialize<Dictionary<string, object?>>(tc.Arguments, JsonOptions);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to deserialize tool call arguments for {Name}", tc.Name);
                        }
                    }
                    chatMessage.Contents.Add(new FunctionCallContent(tc.CallId ?? "", tc.Name, args));
                }
            }

            // Restore tool results
            if (msg.ToolResults is not null)
            {
                foreach (var tr in msg.ToolResults)
                {
                    chatMessage.Contents.Add(new FunctionResultContent(tr.CallId ?? "", tr.Result));
                }
            }

            conversation.AddMessage(chatMessage);
        }

        return conversation;
    }

    public List<SessionData> List()
    {
        if (!Directory.Exists(SessionDir))
            return [];

        var sessions = new List<SessionData>();

        foreach (var file in Directory.GetFiles(SessionDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var session = JsonSerializer.Deserialize<SessionData>(json, JsonOptions);
                if (session is not null)
                    sessions.Add(session);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read session file: {File}", file);
            }
        }

        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    public bool Delete(string id)
    {
        var path = GetSessionPath(id);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        _logger.LogInformation("Session deleted: {Id}", id);
        return true;
    }

    private static SessionData ConversationToSession(Conversation conversation)
    {
        var session = new SessionData
        {
            SystemPrompt = conversation.SystemPrompt,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var msg in conversation.Messages)
        {
            var sessionMsg = new SessionMessage
            {
                Role = msg.Role.Value,
                Text = string.Join("", msg.Contents.OfType<TextContent>().Select(t => t.Text))
            };

            var toolCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            if (toolCalls.Count > 0)
            {
                sessionMsg.ToolCalls = toolCalls.Select(tc => new SessionToolCall
                {
                    CallId = tc.CallId,
                    Name = tc.Name,
                    Arguments = tc.Arguments is not null
                        ? JsonSerializer.Serialize(tc.Arguments)
                        : null
                }).ToList();
            }

            var toolResults = msg.Contents.OfType<FunctionResultContent>().ToList();
            if (toolResults.Count > 0)
            {
                sessionMsg.ToolResults = toolResults.Select(tr => new SessionToolResult
                {
                    CallId = tr.CallId,
                    Result = tr.Result?.ToString() ?? ""
                }).ToList();
            }

            session.Messages.Add(sessionMsg);
        }

        return session;
    }

    private static string GenerateTitle(Conversation conversation)
    {
        // Use first user message as title, truncated
        var firstUserMsg = conversation.Messages
            .FirstOrDefault(m => m.Role == ChatRole.User);

        var text = firstUserMsg?.Text ?? "Untitled";
        return text.Length > 60 ? text[..57] + "..." : text;
    }

    private static string GetSessionPath(string id) =>
        Path.Combine(SessionDir, $"{id}.json");
}
