using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Cli.Rendering;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Session;
using OpenOrca.Tools.Registry;
using Spectre.Console;

namespace OpenOrca.Cli.Repl;

public sealed class ReplLoop
{
    private readonly ConversationManager _conversationManager;
    private readonly OrcaConfig _config;
    private readonly InputHandler _inputHandler;
    private readonly CommandParser _commandParser;
    private readonly SessionManager _sessionManager;
    private readonly ToolCallRenderer _toolCallRenderer;
    private readonly ILogger<ReplLoop> _logger;

    private readonly ReplState _state = new();
    private readonly SystemPromptBuilder _systemPromptBuilder;
    private readonly CommandHandler _commandHandler;
    private readonly ToolCallExecutor _toolCallExecutor;
    private readonly AgentLoopRunner _agentLoopRunner;

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
        ILogger<ReplLoop> logger)
    {
        _conversationManager = conversationManager;
        _config = config;
        _inputHandler = inputHandler;
        _commandParser = commandParser;
        _sessionManager = sessionManager;
        _toolCallRenderer = toolCallRenderer;
        _logger = logger;

        _systemPromptBuilder = new SystemPromptBuilder(config, promptManager);
        var configEditor = new ConfigEditor(config, configManager, logger);
        _toolCallExecutor = new ToolCallExecutor(toolRegistry, toolCallRenderer, _state, logger);
        var toolCallParser = new ToolCallParser(logger);

        _commandHandler = new CommandHandler(
            chatClient, config, configManager, sessionManager, toolCallRenderer,
            conversationManager, _systemPromptBuilder, configEditor, _state, logger);

        _agentLoopRunner = new AgentLoopRunner(
            chatClient, config, streamingRenderer, toolCallParser,
            _toolCallExecutor, _commandHandler, _state, logger);
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

        await _agentLoopRunner.RunAgentLoopAsync(conversation, ct);

        var result = new
        {
            response = _state.LastAssistantResponse ?? "",
            tokens = _state.TotalOutputTokens
        };

        // Write JSON to stdout (using System.Text.Json which is already imported in the project)
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await _commandHandler.ShowWelcomeBannerAsync(ct);

        var conversation = _conversationManager.Active;
        conversation.AddSystemMessage(await _systemPromptBuilder.GetSystemPromptAsync(_toolCallExecutor.Tools, _state.PlanMode));

        while (!ct.IsCancellationRequested)
        {
            // Show plan mode indicator in prompt
            if (_state.PlanMode)
                AnsiConsole.Markup("[cyan][plan][/] ");

            var input = _inputHandler.ReadInput();

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
                // /ask — chat without tools
                if (command.Command == SlashCommand.Ask)
                {
                    if (command.Args.Length == 0)
                    {
                        AnsiConsole.MarkupLine("[yellow]Usage: /ask <question>[/]");
                        continue;
                    }

                    var question = string.Join(" ", command.Args);
                    conversation.AddUserMessage(question);

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
                    finally
                    {
                        _toolCallExecutor.Tools = savedTools;
                    }

                    AnsiConsole.WriteLine();
                    continue;
                }

                if (await _commandHandler.HandleCommandAsync(command, conversation, ct))
                    break;
                continue;
            }

            conversation.AddUserMessage(input);

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
    }
}
