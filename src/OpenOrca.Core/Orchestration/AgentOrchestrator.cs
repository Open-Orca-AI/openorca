using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;

namespace OpenOrca.Core.Orchestration;

public sealed class AgentOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly OrcaConfig _config;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly List<AgentContext> _agents = [];

    // Tool executor callback — same as the main REPL uses, but may have restricted permissions
    public Func<string, string, CancellationToken, Task<string>>? ToolExecutor { get; set; }
    public IList<AITool>? Tools { get; set; }

    public AgentOrchestrator(IChatClient chatClient, OrcaConfig config, ILogger<AgentOrchestrator> logger)
    {
        _chatClient = chatClient;
        _config = config;
        _logger = logger;
    }

    public async Task<AgentContext> SpawnAgentAsync(string task, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(task);

        var context = new AgentContext
        {
            Task = task,
            StartedAt = DateTime.UtcNow
        };

        context.Status = AgentStatus.Running;
        _agents.Add(context);

        _logger.LogInformation("Spawning sub-agent {Id}: {Task}", context.Id, task);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_config.Agent.TimeoutSeconds));
            await RunAgentAsync(context, timeoutCts.Token);
            context.Status = AgentStatus.Completed;
            context.CompletedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            context.Status = AgentStatus.Cancelled;
            context.Error = "Agent was cancelled.";
        }
        catch (Exception ex)
        {
            context.Status = AgentStatus.Failed;
            context.Error = ex.Message;
            _logger.LogError(ex, "Sub-agent {Id} failed", context.Id);
        }

        return context;
    }

    public async Task<List<AgentContext>> SpawnParallelAsync(
        IEnumerable<string> tasks, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var agentTasks = tasks.Select(task => SpawnAgentAsync(task, ct));
        var results = await Task.WhenAll(agentTasks);
        return results.ToList();
    }

    public IReadOnlyList<AgentContext> GetAllAgents() => _agents;

    private async Task RunAgentAsync(AgentContext context, CancellationToken ct)
    {
        var maxIterations = _config.Agent.MaxIterations;

        context.Conversation.AddSystemMessage(
            $"""
            You are a focused sub-agent. Complete the following task and return a concise result.
            Task: {context.Task}

            Current working directory: {Directory.GetCurrentDirectory()}
            Platform: {Environment.OSVersion}

            Use tools to accomplish the task. Be efficient and focused.
            When done, provide a clear summary of what you found or accomplished.
            """);

        context.Conversation.AddUserMessage(context.Task);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            context.IterationCount = iteration + 1;

            var options = new ChatOptions
            {
                Temperature = _config.LmStudio.Temperature,
                MaxOutputTokens = _config.LmStudio.MaxTokens,
                Tools = Tools ?? []
            };

            var response = await _chatClient.GetResponseAsync(
                context.Conversation.GetMessagesForApi(), options, ct);

            foreach (var msg in response.Messages)
            {
                context.Conversation.AddMessage(msg);

                var text = string.Join("", msg.Contents.OfType<TextContent>().Select(t => t.Text));
                var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();

                if (functionCalls.Count == 0)
                {
                    // No more tool calls — agent is done
                    context.Result = text;
                    return;
                }

                // Execute tool calls
                foreach (var call in functionCalls)
                {
                    var toolName = call.Name;
                    var args = call.Arguments is not null
                        ? JsonSerializer.Serialize(call.Arguments)
                        : "{}";

                    string result;
                    if (ToolExecutor is not null)
                    {
                        try
                        {
                            result = await ToolExecutor(toolName, args, ct);
                        }
                        catch (Exception ex)
                        {
                            result = $"Error: {ex.Message}";
                        }
                    }
                    else
                    {
                        result = "Tool execution not available.";
                    }

                    if (_config.LmStudio.NativeToolCalling)
                    {
                        var toolResultMessage = new ChatMessage(ChatRole.Tool, "");
                        toolResultMessage.Contents.Add(new FunctionResultContent(call.CallId, result));
                        context.Conversation.AddMessage(toolResultMessage);
                    }
                    else
                    {
                        // Text-based models cannot handle FunctionResultContent messages
                        context.Conversation.AddMessage(
                            new ChatMessage(ChatRole.User, $"[Tool result for {toolName}]: {result}"));
                    }
                }
            }
        }

        context.Result = "Agent reached maximum iterations without completing.";
    }
}
