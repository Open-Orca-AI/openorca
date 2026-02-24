using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Cli.CustomCommands;
using OpenOrca.Cli.Rendering;
using OpenOrca.Cli.Serialization;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Orchestration;
using OpenOrca.Core.Session;
using OpenOrca.Tools.Registry;
using Spectre.Console;

namespace OpenOrca.Cli.Repl;

public sealed class ReplLoop
{
    private readonly IChatClient _chatClient;
    private readonly ConversationManager _conversationManager;
    private readonly OrcaConfig _config;
    private readonly InputHandler _inputHandler;
    private readonly CommandParser _commandParser;
    private readonly SessionManager _sessionManager;
    private readonly ToolCallRenderer _toolCallRenderer;
    private readonly TerminalPanel _panel;
    private readonly ILogger<ReplLoop> _logger;
    private readonly MemoryManager _memoryManager;

    private readonly ReplState _state;
    private readonly SystemPromptBuilder _systemPromptBuilder;
    private readonly CommandHandler _commandHandler;
    private readonly ToolCallExecutor _toolCallExecutor;
    private readonly AgentLoopRunner _agentLoopRunner;
    private readonly Context7Helper _context7Helper;
    private readonly DocsPreprocessor _docsPreprocessor;

    public ReplLoop(
        IChatClient chatClient,
        ConversationManager conversationManager,
        OrcaConfig config,
        ConfigManager configManager,
        PromptManager promptManager,
        InputHandler inputHandler,
        CommandParser commandParser,
        StreamingRenderer streamingRenderer,
        ToolCallRenderer toolCallRenderer,
        SessionManager sessionManager,
        ToolRegistry toolRegistry,
        TerminalPanel panel,
        ReplState state,
        CheckpointManager checkpointManager,
        CustomCommandLoader customCommandLoader,
        MemoryManager memoryManager,
        AgentOrchestrator orchestrator,
        ILogger<ReplLoop> logger)
    {
        _chatClient = chatClient;
        _conversationManager = conversationManager;
        _config = config;
        _inputHandler = inputHandler;
        _commandParser = commandParser;
        _sessionManager = sessionManager;
        _toolCallRenderer = toolCallRenderer;
        _panel = panel;
        _state = state;
        _logger = logger;
        _memoryManager = memoryManager;

        _systemPromptBuilder = new SystemPromptBuilder(config, promptManager, memoryManager);
        var configEditor = new ConfigEditor(config, configManager, logger);
        _toolCallExecutor = new ToolCallExecutor(toolRegistry, toolCallRenderer, _state, config, logger);
        var toolCallParser = new ToolCallParser(logger);

        // Discover custom commands and register with parser
        customCommandLoader ??= new CustomCommandLoader();
        var customCommands = customCommandLoader.DiscoverCommands();
        commandParser.SetCustomCommandNames(customCommands.Keys);

        // Discover custom agent definitions and register with the type registry
        var customAgentLoader = new CustomAgentLoader();
        var toolNames = toolRegistry.GetToolNames();
        var customAgents = customAgentLoader.DiscoverAgents(toolNames);
        AgentTypeRegistry.RegisterCustom(customAgents);

        _commandHandler = new CommandHandler(
            chatClient, config, configManager, sessionManager, toolCallRenderer,
            conversationManager, _systemPromptBuilder, configEditor, panel, _state, logger,
            checkpointManager, customCommandLoader, memoryManager, orchestrator);

        _agentLoopRunner = new AgentLoopRunner(
            chatClient, config, streamingRenderer, toolCallParser,
            _toolCallExecutor, _commandHandler, _state, logger);

        _context7Helper = new Context7Helper(toolRegistry);
        _docsPreprocessor = new DocsPreprocessor(_context7Helper);
        _commandHandler.RunAgentLoop = _agentLoopRunner.RunAgentLoopAsync;
        _commandHandler.StreamingRenderer = streamingRenderer;
        _commandHandler.Context7 = _context7Helper;
    }

    public void SetTools(IList<AITool> tools, Func<string, string, CancellationToken, Task<string>> executor)
    {
        _toolCallExecutor.Tools = tools;
        _toolCallExecutor.ToolExecutor = executor;
        _commandHandler.Tools = tools;
    }

    public void SetSessionId(string sessionId) => _state.CurrentSessionId = sessionId;

    /// <summary>
    /// Try to cancel the current generation. Returns true if a generation was cancelled.
    /// </summary>
    public bool TryCancelGeneration() => _agentLoopRunner.TryCancelGeneration();

    /// <summary>
    /// Run a single prompt through the full pipeline and exit. Used for testing/CI.
    /// </summary>
    public async Task RunSinglePromptAsync(string prompt, CancellationToken ct)
    {
        var conversation = _conversationManager.Active;
        conversation.AddSystemMessage(await _systemPromptBuilder.GetSystemPromptAsync(_toolCallExecutor.Tools, _state.PlanMode));
        conversation.AddUserMessage(prompt);

        _logger.LogInformation("Single-prompt mode: {Prompt}", prompt);
        AnsiConsole.MarkupLine($"[grey]Prompt:[/] {Markup.Escape(prompt)}");
        AnsiConsole.WriteLine();

        await _agentLoopRunner.RunAgentLoopAsync(conversation, ct);
    }

    /// <summary>
    /// Run a single prompt and output the result as JSON. Used for CI/CD pipelines.
    /// </summary>
    public async Task RunSinglePromptJsonAsync(string prompt, CancellationToken ct)
    {
        // Suppress all Spectre.Console output during JSON mode
        var conversation = _conversationManager.Active;
        conversation.AddSystemMessage(await _systemPromptBuilder.GetSystemPromptAsync(_toolCallExecutor.Tools, _state.PlanMode));
        conversation.AddUserMessage(prompt);

        _logger.LogInformation("Single-prompt JSON mode: {Prompt}", prompt);

        var sw = Stopwatch.StartNew();
        await _agentLoopRunner.RunAgentLoopAsync(conversation, ct);
        sw.Stop();

        var lastToolError = _state.ToolCallHistory.Count > 0 && _state.ToolCallHistory[^1].IsError;
        var result = new SinglePromptResult
        {
            Response = _state.LastAssistantResponse ?? "",
            Tokens = _state.TotalOutputTokens,
            DurationMs = sw.ElapsedMilliseconds,
            ToolCalls = _state.ToolCallHistory.Count > 0 ? _state.ToolCallHistory.ToList() : null,
            FilesModified = _state.FilesModified.Count > 0 ? _state.FilesModified.ToList() : null,
            Success = !lastToolError
        };

        Console.WriteLine(JsonSerializer.Serialize(result, OrcaCliJsonContext.Default.SinglePromptResult));
    }

    /// <summary>
    /// Run a single turn without tools (used by /ask and persistent Ask mode).
    /// </summary>
    private async Task RunNoToolsTurnAsync(Conversation conversation, string userMessage, CancellationToken ct)
    {
        conversation.AddUserMessage(userMessage);

        var savedTools = _toolCallExecutor.Tools;
        try
        {
            _toolCallExecutor.Tools = [];
            var turnStopwatch = Stopwatch.StartNew();
            await _agentLoopRunner.RunAgentLoopAsync(conversation, ct);
            turnStopwatch.Stop();
            _state.TotalTurns++;
            AnsiConsole.Markup($"[dim][[{turnStopwatch.Elapsed.TotalSeconds:F1}s]][/]");
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("\n[yellow]Cancelled.[/]");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in no-tools agent loop");
            AnsiConsole.MarkupLine($"\n[red]Error: {Markup.Escape(ex.Message)}[/]");
            AgentLoopRunner.ShowLogHint();
        }
        finally
        {
            _toolCallExecutor.Tools = savedTools;
        }

        AnsiConsole.WriteLine();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        AnsiConsole.Clear();
        _panel.Setup();
        await _commandHandler.ShowWelcomeBannerAsync(ct);

        var conversation = _conversationManager.Active;
        conversation.AddSystemMessage(await _systemPromptBuilder.GetSystemPromptAsync(_toolCallExecutor.Tools, _state.PlanMode));

        while (!ct.IsCancellationRequested)
        {
            var input = await _inputHandler.ReadInputAsync(ct);

            if (input is null)
                break;

            if (string.IsNullOrWhiteSpace(input))
                continue;

            // ! bash shortcut — run shell command directly
            if (input.StartsWith('!') && input.Length > 1)
            {
                await _commandHandler.ExecuteBashShortcutAsync(input[1..].Trim());
                continue;
            }

            // Check for slash commands
            var command = _commandParser.TryParse(input);
            if (command is not null)
            {
                // /ask with args — one-shot chat without tools
                if (command.Command == SlashCommand.Ask && command.Args.Length > 0)
                {
                    var question = string.Join(" ", command.Args);
                    await RunNoToolsTurnAsync(conversation, question, ct);
                    continue;
                }

                if (await _commandHandler.HandleCommandAsync(command, conversation, ct))
                    break;

                // Custom commands inject a user message — fall through to run the agent loop
                if (command.Command != SlashCommand.CustomCommand)
                    continue;
            }
            else
            {
                // Persistent Ask mode — run without tools
                if (_state.Mode == InputMode.Ask)
                {
                    await RunNoToolsTurnAsync(conversation, input, ct);
                    continue;
                }

                input = InputPreprocessor.ExpandFileReferences(input);
                input = await _docsPreprocessor.ExpandDocsReferencesAsync(input, ct);
                AnsiConsole.MarkupLine($"[on darkblue]\U0001f477 user: {Markup.Escape(input)}[/]");
                conversation.AddUserMessage(input);
            }

            try
            {
                var turnStopwatch = Stopwatch.StartNew();
                await _agentLoopRunner.RunAgentLoopAsync(conversation, ct);
                turnStopwatch.Stop();
                _state.TotalTurns++;

                // Show subtle turn timing
                AnsiConsole.Markup($"[dim][[{turnStopwatch.Elapsed.TotalSeconds:F1}s]][/]");

                // Plan mode approval flow
                if (_state.PlanMode)
                {
                    AnsiConsole.WriteLine();
                    var choice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[cyan]Plan complete. What would you like to do?[/]")
                            .AddChoices("Approve & Execute", "Modify plan", "Discard"));

                    switch (choice)
                    {
                        case "Approve & Execute":
                            _state.PlanMode = false;
                            _toolCallRenderer.RenderPlanModeToggle(false);
                            conversation.AddSystemMessage(await _systemPromptBuilder.GetSystemPromptAsync(_toolCallExecutor.Tools, _state.PlanMode));
                            conversation.AddUserMessage(
                                "The plan is approved. Please execute it now. " +
                                "Make all the changes described in the plan above.");
                            _logger.LogInformation("Plan approved — executing with full tool access");
                            await _agentLoopRunner.RunAgentLoopAsync(conversation, ct);
                            break;

                        case "Modify plan":
                            AnsiConsole.MarkupLine("[yellow]Type your modifications below — the model will revise the plan.[/]");
                            break;

                        case "Discard":
                            AnsiConsole.MarkupLine("[grey]Plan discarded.[/]");
                            break;
                    }
                }

                // Auto-save session
                if (_config.Session.AutoSave)
                {
                    try
                    {
                        _state.CurrentSessionId = await _sessionManager.SaveAsync(
                            conversation, existingId: _state.CurrentSessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to auto-save session");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("\n[yellow]Cancelled.[/]");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in agent loop");
                AnsiConsole.MarkupLine($"\n[red]Error: {Markup.Escape(ex.Message)}[/]");
                AgentLoopRunner.ShowLogHint();
            }

            AnsiConsole.WriteLine();
        }

        // Auto-save session learnings at exit
        await SaveAutoMemoryAsync(conversation, ct);
    }

    private async Task SaveAutoMemoryAsync(Conversation conversation, CancellationToken ct)
    {
        if (!_config.Memory.AutoMemoryEnabled)
            return;

        // Only save if there was meaningful tool usage (at least 2 turns)
        if (_state.TotalTurns < 2)
            return;

        try
        {
            var summaryMessages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(Microsoft.Extensions.AI.ChatRole.System,
                    "Based on this session, what project-specific patterns, conventions, or learnings should be remembered? " +
                    "Write concise bullet points. Only include things that would be useful in future sessions. " +
                    "If there's nothing noteworthy to remember, respond with just 'NONE'.")
            };

            // Add a summary of the conversation (last few user/assistant messages)
            var relevantMessages = conversation.Messages
                .Where(m => m.Role == Microsoft.Extensions.AI.ChatRole.User || m.Role == Microsoft.Extensions.AI.ChatRole.Assistant)
                .TakeLast(10);

            foreach (var msg in relevantMessages)
            {
                var text = msg.Text ?? string.Join("", msg.Contents.OfType<Microsoft.Extensions.AI.TextContent>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var truncated = text.Length > 2000 ? text[..2000] + "..." : text;
                    summaryMessages.Add(new Microsoft.Extensions.AI.ChatMessage(msg.Role, truncated));
                }
            }

            var options = new Microsoft.Extensions.AI.ChatOptions
            {
                Temperature = 0.3f,
                MaxOutputTokens = 500,
            };
            options.ModelId ??= _config.LmStudio.Model;

            using var memoryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            memoryCts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _chatClient.GetResponseAsync(summaryMessages, options, memoryCts.Token);
            var learnings = string.Join("", response.Messages
                .SelectMany(m => m.Contents.OfType<Microsoft.Extensions.AI.TextContent>())
                .Select(t => t.Text)).Trim();

            if (!string.IsNullOrWhiteSpace(learnings) &&
                !learnings.Equals("NONE", StringComparison.OrdinalIgnoreCase) &&
                !learnings.StartsWith("NONE", StringComparison.OrdinalIgnoreCase))
            {
                await _memoryManager.SaveLearningsAsync(learnings);
                AnsiConsole.MarkupLine("[dim]Saved session learnings to memory.[/]");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to save auto memory (non-critical)");
        }
    }
}
