using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Serialization;

namespace OpenOrca.Core.Orchestration;

public sealed class AgentOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly OrcaConfig _config;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly List<AgentContext> _agents = [];
    private readonly AgentPromptLoader _promptLoader = new();

    // Tool executor callback — same as the main REPL uses, but may have restricted permissions
    public Func<string, string, CancellationToken, Task<string>>? ToolExecutor { get; set; }
    public IList<AITool>? Tools { get; set; }

    public AgentOrchestrator(IChatClient chatClient, OrcaConfig config, ILogger<AgentOrchestrator> logger)
    {
        _chatClient = chatClient;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Spawn a sub-agent with the default "general" type.
    /// </summary>
    public Task<AgentContext> SpawnAgentAsync(string task, CancellationToken ct)
        => SpawnAgentAsync(task, "general", ct);

    /// <summary>
    /// Spawn a typed sub-agent. The agent type determines the system prompt and allowed tools.
    /// </summary>
    public async Task<AgentContext> SpawnAgentAsync(string task, string agentType, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(task);

        var typeDef = AgentTypeRegistry.Resolve(agentType) ?? AgentTypeRegistry.GetDefault();

        var context = new AgentContext
        {
            Task = task,
            AgentType = typeDef.Name,
            StartedAt = DateTime.UtcNow
        };

        context.Status = AgentStatus.Running;
        _agents.Add(context);

        _logger.LogInformation("Spawning {AgentType} sub-agent {Id}: {Task}", typeDef.Name, context.Id, task);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_config.Agent.TimeoutSeconds));
            await RunAgentAsync(context, typeDef, timeoutCts.Token);
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

    private async Task RunAgentAsync(AgentContext context, AgentTypeDefinition typeDef, CancellationToken ct)
    {
        var maxIterations = _config.Agent.MaxIterations;
        var cwd = Directory.GetCurrentDirectory();
        var platform = Environment.OSVersion.ToString();

        // Load typed prompt, fall back to generic prompt
        var systemPrompt = _promptLoader.LoadPrompt(typeDef, context.Task, cwd, platform);
        if (systemPrompt is null)
        {
            systemPrompt = $"""
                You are a focused sub-agent. Complete the following task and return a concise result.
                Task: {context.Task}

                Current working directory: {cwd}
                Platform: {platform}

                Use tools to accomplish the task. Be efficient and focused.
                When done, provide a clear summary of what you found or accomplished.
                """;
        }

        context.Conversation.AddSystemMessage(systemPrompt);
        context.Conversation.AddUserMessage(context.Task);

        // Filter tools for this agent type
        var filteredTools = FilterToolsForType(typeDef);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            context.IterationCount = iteration + 1;

            var options = new ChatOptions
            {
                Temperature = _config.LmStudio.Temperature,
                MaxOutputTokens = _config.LmStudio.MaxTokens,
                Tools = filteredTools
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

                    // Defense-in-depth: reject disallowed tools even if text-based parsing let them through
                    if (!IsToolAllowed(typeDef, toolName))
                    {
                        _logger.LogWarning(
                            "Agent {Id} ({Type}) attempted disallowed tool: {Tool}",
                            context.Id, typeDef.Name, toolName);

                        var rejectionMsg = $"Tool '{toolName}' is not available to {typeDef.Name} agents. " +
                            $"Allowed tools: {string.Join(", ", typeDef.AllowedTools ?? ["all"])}.";

                        AddToolResult(context, call, rejectionMsg);
                        continue;
                    }

                    var args = call.Arguments is not null
                        ? JsonSerializer.Serialize(call.Arguments, OrcaJsonContext.Default.IDictionaryStringObject)
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

                    AddToolResult(context, call, result);
                }
            }
        }

        context.Result = "Agent reached maximum iterations without completing.";
    }

    /// <summary>
    /// Filter the full tool list to only include tools allowed by the agent type definition.
    /// Returns all tools if AllowedTools is null (unrestricted).
    /// </summary>
    public IList<AITool> FilterToolsForType(AgentTypeDefinition typeDef)
    {
        if (typeDef.AllowedTools is null)
            return Tools ?? [];

        var allowedSet = new HashSet<string>(typeDef.AllowedTools, StringComparer.OrdinalIgnoreCase);
        return (Tools ?? [])
            .Where(t => t is AIFunction fn && allowedSet.Contains(fn.Name))
            .ToList();
    }

    /// <summary>
    /// Check whether a tool name is allowed for the given agent type.
    /// Returns true if AllowedTools is null (unrestricted).
    /// </summary>
    public static bool IsToolAllowed(AgentTypeDefinition typeDef, string toolName)
    {
        if (typeDef.AllowedTools is null)
            return true;

        return typeDef.AllowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase);
    }

    private void AddToolResult(AgentContext context, FunctionCallContent call, string result)
    {
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
                new ChatMessage(ChatRole.User, $"[Tool result for {call.Name}]: {result}"));
        }
    }
}
