using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace OpenOrca.Core.Client;

/// <summary>
/// Embedded mock chat client for demo GIF recording.
/// Returns canned responses with text-based tool calls, streaming word-by-word.
/// Activated by the --demo CLI flag — no external server required.
/// </summary>
public sealed class DemoChatClient : IChatClient
{
    private const int TokenDelayMs = 8;

    private const string ToolCallReadFile =
        "Let me start by reading the README to understand this project.\n\n"
        + "<tool_call>{\"name\":\"read_file\",\"arguments\":{\"path\":\"README.md\"}}</tool_call>";

    private const string ToolCallListDir =
        "Good, I can see the README. Let me also check the project structure.\n\n"
        + "<tool_call>{\"name\":\"list_directory\",\"arguments\":{\"path\":\".\"}}</tool_call>";

    private const string FinalSummary =
        "Here's a summary of the **OpenOrca** project:\n\n"
        + "**OpenOrca** is an autonomous AI coding agent that runs in your terminal. "
        + "It connects to local LLM servers like LM Studio or Ollama via an OpenAI-compatible API "
        + "and uses **31 built-in tools** to read, write, and execute code autonomously.\n\n"
        + "**Key highlights:**\n\n"
        + "- **Autonomous agent loop** — plans, acts, observes, and iterates up to 25 turns per request\n"
        + "- **31 tools** — file I/O, shell execution, git operations, web search, GitHub integration, and sub-agent spawning\n"
        + "- **Works with any local model** — Mistral, Llama, DeepSeek, Qwen, and more\n"
        + "- **Smart tool calling** — auto-detects native function calling support, falls back to text-based `<tool_call>` tags\n"
        + "- **Rich CLI** — streaming output, thinking indicator, slash commands, session management, context compaction\n"
        + "- **Privacy-first** — everything runs locally, no data leaves your machine\n\n"
        + "The project is structured as a .NET 9 solution with three main assemblies: "
        + "`OpenOrca.Cli` (console UI), `OpenOrca.Core` (domain logic), and `OpenOrca.Tools` (tool implementations). "
        + "It's MIT licensed and designed as a local, open-source alternative to cloud-based AI coding assistants.";

    public ChatClientMetadata Metadata { get; } = new("DemoChatClient", null, "demo-model");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = PickResponse(chatMessages);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var text = PickResponse(chatMessages);
        var words = text.Split(' ');

        for (var i = 0; i < words.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = i == 0 ? words[i] : " " + words[i];

            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(token)],
            };

            await Task.Delay(TokenDelayMs, cancellationToken);
        }

        // Final update with finish reason
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            FinishReason = ChatFinishReason.Stop,
        };
    }

    public void Dispose()
    {
        // No-op
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ChatClientMetadata))
            return Metadata;
        if (serviceType.IsInstanceOfType(this))
            return this;
        return null;
    }

    private static string PickResponse(IEnumerable<ChatMessage> messages)
    {
        // Count tool results: user messages containing "[Tool result for " markers
        // (matches ToolCallExecutor.ExecuteTextToolCallsAsync format)
        var toolResultCount = 0;
        foreach (var msg in messages)
        {
            if (msg.Role != ChatRole.User) continue;
            var text = msg.Text ?? string.Join("", msg.Contents.OfType<TextContent>().Select(t => t.Text));
            if (text.Contains("[Tool result for "))
                toolResultCount++;
        }

        return toolResultCount switch
        {
            0 => ToolCallReadFile,
            1 => ToolCallListDir,
            _ => FinalSummary,
        };
    }
}
