using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Cli.Rendering;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Session;
using Spectre.Console;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Handles all slash commands dispatched from the REPL loop.
/// </summary>
internal sealed class CommandHandler
{
    private readonly IChatClient _chatClient;
    private readonly OrcaConfig _config;
    private readonly ConfigManager _configManager;
    private readonly SessionManager _sessionManager;
    private readonly ToolCallRenderer _toolCallRenderer;
    private readonly ConversationManager _conversationManager;
    private readonly SystemPromptBuilder _systemPromptBuilder;
    private readonly ConfigEditor _configEditor;
    private readonly ReplState _state;
    private readonly ILogger _logger;

    public IList<AITool>? Tools { get; set; }

    public CommandHandler(
        IChatClient chatClient,
        OrcaConfig config,
        ConfigManager configManager,
        SessionManager sessionManager,
        ToolCallRenderer toolCallRenderer,
        ConversationManager conversationManager,
        SystemPromptBuilder systemPromptBuilder,
        ConfigEditor configEditor,
        ReplState state,
        ILogger logger)
    {
        _chatClient = chatClient;
        _config = config;
        _configManager = configManager;
        _sessionManager = sessionManager;
        _toolCallRenderer = toolCallRenderer;
        _conversationManager = conversationManager;
        _systemPromptBuilder = systemPromptBuilder;
        _configEditor = configEditor;
        _state = state;
        _logger = logger;
    }

    /// <summary>
    /// Handle a parsed slash command. Returns true if the REPL should exit.
    /// </summary>
    public async Task<bool> HandleCommandAsync(ParsedCommand command, Conversation conversation, CancellationToken ct)
    {
        switch (command.Command)
        {
            case SlashCommand.Exit:
                AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                return true;

            case SlashCommand.Help:
                ShowHelp();
                return false;

            case SlashCommand.Clear:
                conversation.Clear();
                conversation.AddSystemMessage(await _systemPromptBuilder.GetSystemPromptAsync(Tools, _state.PlanMode));
                AnsiConsole.Clear();
                ShowWelcomeBannerMinimal();
                return false;

            case SlashCommand.Model:
                await HandleModelCommandAsync(command.Args, ct);
                return false;

            case SlashCommand.Config:
                await _configEditor.ShowConfigAsync();
                return false;

            case SlashCommand.Session:
                await HandleSessionCommandAsync(command.Args, conversation, ct);
                return false;

            case SlashCommand.Plan:
                await HandlePlanCommandAsync(command.Args, conversation);
                return false;

            case SlashCommand.Compact:
                await HandleCompactAsync(command.Args, conversation, ct);
                return false;

            case SlashCommand.Rewind:
                HandleRewind(command.Args, conversation);
                return false;

            case SlashCommand.Context:
                ShowContext(conversation);
                return false;

            case SlashCommand.Stats:
                ShowStats(conversation);
                return false;

            case SlashCommand.Memory:
                await HandleMemoryCommandAsync(command.Args);
                return false;

            case SlashCommand.Doctor:
                await RunDoctorAsync(ct);
                return false;

            case SlashCommand.Copy:
                HandleCopy();
                return false;

            case SlashCommand.Export:
                HandleExport(command.Args, conversation);
                return false;

            default:
                AnsiConsole.MarkupLine("[yellow]Unknown command. Type /help for available commands.[/]");
                return false;
        }
    }

    public async Task ExecuteBashShortcutAsync(string command)
    {
        _logger.LogInformation("Bash shortcut: {Command}", command);

        try
        {
            string shell;
            string shellArgs;

            if (OperatingSystem.IsWindows())
            {
                shell = "cmd.exe";
                shellArgs = $"/c {command}";
            }
            else
            {
                shell = "/bin/bash";
                shellArgs = $"-c \"{command.Replace("\"", "\\\"")}\"";
            }

            var psi = new ProcessStartInfo(shell, shellArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                AnsiConsole.MarkupLine("[red]Failed to start shell process.[/]");
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);

            // Truncate for display
            if (stdout.Length > 5000)
                stdout = stdout[..5000] + "\n... (truncated)";
            if (stderr.Length > 5000)
                stderr = stderr[..5000] + "\n... (truncated)";

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                AnsiConsole.Write(new Panel(Markup.Escape(stdout.TrimEnd()))
                    .Header($"[green]$ {Markup.Escape(command)}[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Green));
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                AnsiConsole.Write(new Panel(Markup.Escape(stderr.TrimEnd()))
                    .Header("[red]stderr[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Red));
            }

            if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr))
            {
                AnsiConsole.MarkupLine($"[grey]$ {Markup.Escape(command)} (no output)[/]");
            }

            var exitColor = proc.ExitCode == 0 ? "green" : "red";
            AnsiConsole.MarkupLine($"[{exitColor}]Exit code: {proc.ExitCode}[/]");
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Command timed out (120s).[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to run command: {Markup.Escape(ex.Message)}[/]");
        }
    }

    public async Task ShowWelcomeBannerAsync(CancellationToken ct = default)
    {
        // ── Combined orca + "OpenOrca" ANSI banner ──
        var bannerLines = new[]
        {
            "\x1b[48;5;69m        \x1b[38;5;69;48;5;69m\u2584\x1b[38;5;24;48;5;69m\u2584\u2584\u2584\x1b[38;5;69;48;5;69m\u2584\x1b[38;5;24;48;5;69m\u2584\u2584\x1b[38;5;25;48;5;69m\u2584\x1b[48;5;69m                                                      \x1b[0m",
            "\x1b[48;5;69m       \x1b[38;5;234;48;5;69m\u2584\x1b[38;5;0;48;5;234m\u2584\x1b[38;5;69;48;5;69m\u2584\x1b[38;5;69;48;5;0m\u2584\x1b[38;5;0;48;5;0m\u2584\u2584\u2584\x1b[38;5;69;48;5;0m\u2584\x1b[38;5;69;48;5;69m\u2584\x1b[48;5;69m   \x1b[38;5;0;48;5;69m\u2584\u2584\u2584\u2584\x1b[48;5;69m                    \x1b[38;5;0;48;5;69m\u2584\u2584\u2584\u2584\x1b[48;5;69m                       \x1b[0m",
            "\x1b[48;5;69m   \x1b[38;5;235;48;5;69m\u2584\u2584\u2584\x1b[38;5;0;48;5;234m\u2584\x1b[38;5;0;48;5;0m\u2584\x1b[48;5;0m \x1b[38;5;234;48;5;69m\u2584\u2584\u2584\x1b[38;5;0;48;5;0m\u2584\x1b[38;5;69;48;5;69m\u2584\u2584\x1b[48;5;69m   \x1b[48;5;0m \x1b[38;5;69;48;5;0m\u2584\x1b[48;5;69m  \x1b[38;5;69;48;5;0m\u2584\x1b[48;5;0m \x1b[48;5;69m \x1b[48;5;0m \x1b[38;5;69;48;5;0m\u2584\u2584\u2584\x1b[38;5;0;48;5;69m\u2584\x1b[48;5;69m \x1b[38;5;0;48;5;69m\u2584\x1b[38;5;69;48;5;0m\u2584\u2584\u2584\x1b[38;5;0;48;5;69m\u2584\x1b[48;5;69m \x1b[48;5;0m \x1b[38;5;69;48;5;0m\u2584\u2584\x1b[48;5;0m \x1b[48;5;69m \x1b[48;5;0m \x1b[38;5;69;48;5;0m\u2584\x1b[48;5;69m  \x1b[38;5;69;48;5;0m\u2584\x1b[48;5;0m \x1b[48;5;69m \x1b[48;5;0m \x1b[38;5;69;48;5;0m\u2584\u2584\x1b[48;5;0m \x1b[48;5;69m \x1b[38;5;0;48;5;69m\u2584\x1b[38;5;69;48;5;0m\u2584\u2584\u2584\x1b[48;5;69m  \x1b[38;5;69;48;5;0m\u2584\u2584\u2584\x1b[38;5;0;48;5;69m\u2584\x1b[48;5;69m      \x1b[0m",
            "\x1b[48;5;69m \x1b[38;5;24;48;5;69m\u2584\x1b[38;5;0;48;5;234m\u2584\x1b[38;5;0;48;5;0m\u2584\x1b[48;5;0m \x1b[38;5;15;48;5;0m\u2584\u2584\x1b[48;5;0m  \x1b[38;5;0;48;5;0m\u2584\x1b[48;5;0m   \x1b[48;5;69m \x1b[38;5;69;48;5;69m\u2584\u2584\x1b[48;5;69m  \x1b[48;5;0m \x1b[48;5;69m    \x1b[48;5;0m \x1b[48;5;69m \x1b[48;5;0m \x1b[48;5;69m   \x1b[48;5;0m \x1b[48;5;69m \x1b[48;5;0m \x1b[38;5;69;48;5;0m\u2584\u2584\u2584\u2584\x1b[48;5;69m \x1b[48;5;0m \x1b[48;5;69m  \x1b[48;5;0m \x1b[48;5;69m \x1b[48;5;0m \x1b[48;5;69m    \x1b[48;5;0m \x1b[48;5;69m \x1b[48;5;0m \x1b[48;5;69m    \x1b[48;5;0m \x1b[48;5;69m    \x1b[38;5;0;48;5;69m\u2584\x1b[38;5;69;48;5;0m\u2584\u2584\u2584\x1b[48;5;0m \x1b[48;5;69m      \x1b[0m",
            "\x1b[48;5;69m \x1b[38;5;69;48;5;24m\u2584\x1b[38;5;15;48;5;0m\u2584\u2584\u2584\u2584\x1b[38;5;0;48;5;0m\u2584\x1b[48;5;0m  \x1b[38;5;15;48;5;0m\u2584\u2584\x1b[38;5;69;48;5;0m\u2584\x1b[38;5;69;48;5;69m\u2584\x1b[48;5;69m     \x1b[38;5;69;48;5;0m\u2584\x1b[48;5;0m \x1b[38;5;0;48;5;69m\u2584\u2584\x1b[48;5;0m \x1b[38;5;69;48;5;0m\u2584\x1b[48;5;69m \x1b[48;5;0m \x1b[38;5;0;48;5;69m\u2584\u2584\u2584\x1b[38;5;69;48;5;0m\u2584\x1b[48;5;69m \x1b[38;5;69;48;5;0m\u2584\x1b[48;5;0m \x1b[38;5;0;48;5;69m\u2584\u2584\u2584\x1b[48;5;69m \x1b[48;5;0m \x1b[48;5;69m  \x1b[48;5;0m \x1b[48;5;69m \x1b[38;5;69;48;5;0m\u2584\x1b[48;5;0m \x1b[38;5;0;48;5;69m\u2584\u2584\x1b[48;5;0m \x1b[38;5;69;48;5;0m\u2584\x1b[48;5;69m \x1b[48;5;0m \x1b[48;5;69m    \x1b[38;5;69;48;5;0m\u2584\x1b[38;5;0;48;5;69m\u2584\u2584\u2584\x1b[48;5;69m \x1b[38;5;69;48;5;0m\u2584\x1b[38;5;0;48;5;69m\u2584\u2584\u2584\x1b[48;5;0m \x1b[48;5;69m      \x1b[0m",
            "\x1b[48;5;69m  \x1b[38;5;69;48;5;69m\u2584\u2584\x1b[38;5;69;48;5;0m\u2584\x1b[38;5;236;48;5;0m\u2584\x1b[38;5;69;48;5;69m\u2584\x1b[38;5;69;48;5;0m\u2584\x1b[38;5;236;48;5;0m\u2584\x1b[38;5;69;48;5;69m\u2584\u2584\u2584\x1b[48;5;69m             \x1b[48;5;0m \x1b[48;5;69m                                            \x1b[0m",
            "\x1b[49;38;5;69m\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\u2580\x1b[0m",
        };

        var consoleWidth = ConsoleHelper.GetConsoleWidth();
        const int artWidth = 70;

        for (var i = 0; i < bannerLines.Length; i++)
        {
            var line = bannerLines[i];

            if (consoleWidth <= artWidth)
            {
                Console.WriteLine(line);
                continue;
            }

            var visible = ConsoleHelper.VisibleLength(line);
            var totalPad = consoleWidth - visible;
            var leftPad = totalPad / 2;
            var rightPad = totalPad - leftPad;

            if (i < bannerLines.Length - 1)
            {
                // Blue-background lines: pad with blue bg spaces
                Console.Write($"\x1b[48;5;69m{new string(' ', leftPad)}");
                Console.Write(line.Replace("\x1b[0m", ""));
                Console.WriteLine($"{new string(' ', rightPad)}\x1b[0m");
            }
            else
            {
                // Floor line: pad with half-block chars using same color
                Console.Write($"\x1b[49;38;5;69m{new string('\u2580', leftPad)}");
                Console.Write(line.Replace("\x1b[0m", ""));
                Console.WriteLine($"{new string('\u2580', rightPad)}\x1b[0m");
            }
        }

        var version = typeof(ReplLoop).Assembly.GetName().Version;
        var versionStr = version is not null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "dev";

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]CLI AI Orchestrator {versionStr} — Type /help for commands, /exit to quit, Ctrl+O to show thinking[/]");

        // ── Connection status ──
        if (_config.DemoMode)
        {
            AnsiConsole.MarkupLine($"[grey]Endpoint:[/]  [green]● Demo mode[/]");
            AnsiConsole.MarkupLine($"[grey]Model:[/]     [cyan]{Markup.Escape(_config.LmStudio.Model ?? "demo-model")}[/]");
        }
        else
        {
            var baseUrl = _config.LmStudio.BaseUrl;
            AnsiConsole.Markup($"[grey]Endpoint:[/]  [white]{Markup.Escape(baseUrl)}[/]  ");

            try
            {
                var discovery = new Core.Client.ModelDiscovery(_config,
                    _logger as ILogger<Core.Client.ModelDiscovery>
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Client.ModelDiscovery>.Instance);
                var models = await discovery.GetAvailableModelsAsync(ct);

                if (models.Count > 0)
                {
                    AnsiConsole.MarkupLine("[green]● Connected[/]");

                    var activeModel = _config.LmStudio.Model;
                    if (!string.IsNullOrEmpty(activeModel))
                    {
                        AnsiConsole.MarkupLine($"[grey]Model:[/]     [cyan]{Markup.Escape(activeModel)}[/]");
                    }
                    else if (models.Count == 1)
                    {
                        AnsiConsole.MarkupLine($"[grey]Model:[/]     [cyan]{Markup.Escape(models[0])}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[grey]Models:[/]    [cyan]{models.Count} available[/] — {Markup.Escape(string.Join(", ", models.Take(3)))}{(models.Count > 3 ? ", ..." : "")}");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]● No models loaded[/]");
                }
            }
            catch (Exception)
            {
                AnsiConsole.MarkupLine("[red]● Unreachable[/]");
            }
        }

        AnsiConsole.WriteLine();
    }

    public async Task CompactConversationAsync(Conversation conversation, string? extraInstructions, CancellationToken ct)
    {
        var preserveLastN = _config.Context.CompactPreserveLastN;
        var toSummarize = conversation.GetMessagesForCompaction(preserveLastN);

        if (toSummarize.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Not enough messages to compact.[/]");
            return;
        }

        var tokensBefore = conversation.EstimateTokenCount();
        var msgCountBefore = conversation.Messages.Count;

        // Build summarization prompt
        var summaryMessages = new List<ChatMessage>();
        var summaryInstructions = "Summarize this conversation concisely. Preserve key decisions, file paths, code changes, and any important context the assistant would need to continue helping.";
        if (extraInstructions is not null)
            summaryInstructions += $" Focus on: {extraInstructions}";

        summaryMessages.Add(new ChatMessage(ChatRole.System, summaryInstructions));
        foreach (var msg in toSummarize)
        {
            var text = msg.Text ?? string.Join("", msg.Contents.OfType<TextContent>().Select(t => t.Text));
            if (!string.IsNullOrWhiteSpace(text))
                summaryMessages.Add(new ChatMessage(msg.Role == ChatRole.Tool ? ChatRole.User : msg.Role,
                    $"[{msg.Role.Value}]: {(text.Length > 2000 ? text[..2000] + "..." : text)}"));
        }

        AnsiConsole.Markup("[yellow]Compacting conversation...[/]");

        try
        {
            var options = new ChatOptions
            {
                Temperature = 0.3f,
                MaxOutputTokens = 500,
            };
            if (_config.LmStudio.Model is not null)
                options.ModelId = _config.LmStudio.Model;

            var response = await _chatClient.GetResponseAsync(summaryMessages, options, ct);
            var summary = string.Join("", response.Messages
                .SelectMany(m => m.Contents.OfType<TextContent>())
                .Select(t => t.Text));

            if (string.IsNullOrWhiteSpace(summary))
            {
                AnsiConsole.MarkupLine("\n[red]Failed to generate summary — empty response.[/]");
                return;
            }

            var removed = conversation.CompactWithSummary(summary, preserveLastN);
            var tokensAfter = conversation.EstimateTokenCount();

            AnsiConsole.MarkupLine($"\n[green]Compacted: {msgCountBefore} messages -> {conversation.Messages.Count} messages (~{tokensBefore} -> ~{tokensAfter} tokens)[/]");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to compact conversation");
            AnsiConsole.MarkupLine($"\n[red]Failed to compact: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private async Task HandleSessionCommandAsync(string[] args, Conversation conversation, CancellationToken ct)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case "list":
                var sessions = _sessionManager.List();
                if (sessions.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]No saved sessions.[/]");
                    return;
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Expand()
                    .AddColumn("ID")
                    .AddColumn("Title")
                    .AddColumn("Updated")
                    .AddColumn("Messages");

                foreach (var s in sessions.Take(20))
                {
                    table.AddRow(
                        s.Id,
                        Markup.Escape(s.Title.Length > 40 ? s.Title[..37] + "..." : s.Title),
                        s.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        s.Messages.Count.ToString());
                }

                AnsiConsole.Write(table);
                break;

            case "save":
                var title = args.Length > 1 ? string.Join(" ", args[1..]) : null;
                var id = await _sessionManager.SaveAsync(conversation, title, _state.CurrentSessionId);
                _state.CurrentSessionId = id;
                AnsiConsole.MarkupLine($"[green]Session saved: {id}[/]");
                break;

            case "load":
                if (args.Length < 2)
                {
                    AnsiConsole.MarkupLine("[yellow]Usage: /session load <id>[/]");
                    return;
                }
                var sessionData = await _sessionManager.LoadAsync(args[1]);
                if (sessionData is null)
                {
                    AnsiConsole.MarkupLine($"[red]Session not found: {Markup.Escape(args[1])}[/]");
                    return;
                }
                var restored = _sessionManager.SessionToConversation(sessionData);
                _conversationManager.SetActive(sessionData.Id, restored);
                _state.CurrentSessionId = sessionData.Id;
                AnsiConsole.MarkupLine($"[green]Session loaded: {Markup.Escape(sessionData.Title)} ({sessionData.Messages.Count} messages)[/]");
                // Update the conversation reference in the outer loop
                conversation.Clear();
                foreach (var msg in restored.Messages)
                    conversation.AddMessage(msg);
                if (restored.SystemPrompt is not null)
                    conversation.AddSystemMessage(restored.SystemPrompt);
                break;

            case "delete":
                if (args.Length < 2)
                {
                    AnsiConsole.MarkupLine("[yellow]Usage: /session delete <id>[/]");
                    return;
                }
                if (_sessionManager.Delete(args[1]))
                    AnsiConsole.MarkupLine($"[green]Session deleted: {args[1]}[/]");
                else
                    AnsiConsole.MarkupLine($"[red]Session not found: {Markup.Escape(args[1])}[/]");
                break;

            default:
                AnsiConsole.MarkupLine("[yellow]Usage: /session list|save [name]|load <id>|delete <id>[/]");
                break;
        }
    }

    private async Task HandlePlanCommandAsync(string[] args, Conversation conversation)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : null;

        switch (action)
        {
            case "on":
                _state.PlanMode = true;
                break;
            case "off":
                _state.PlanMode = false;
                break;
            default:
                _state.PlanMode = !_state.PlanMode;
                break;
        }

        _toolCallRenderer.RenderPlanModeToggle(_state.PlanMode);
        _logger.LogInformation("Plan mode toggled: {Enabled}", _state.PlanMode);

        // Update system prompt to include/exclude plan mode instructions
        conversation.AddSystemMessage(await _systemPromptBuilder.GetSystemPromptAsync(Tools, _state.PlanMode));
    }

    private async Task HandleCompactAsync(string[] args, Conversation conversation, CancellationToken ct)
    {
        var extraInstructions = args.Length > 0 ? string.Join(" ", args) : null;
        await CompactConversationAsync(conversation, extraInstructions, ct);
    }

    private void HandleRewind(string[] args, Conversation conversation)
    {
        var turnCount = 1;
        if (args.Length > 0 && int.TryParse(args[0], out var n) && n > 0)
            turnCount = n;

        var removed = conversation.RemoveLastTurns(turnCount);
        if (removed > 0)
            AnsiConsole.MarkupLine($"[green]Rewound {turnCount} turn(s) (removed {removed} messages).[/]");
        else
            AnsiConsole.MarkupLine("[yellow]No messages to rewind.[/]");
    }

    private void ShowContext(Conversation conversation)
    {
        var estimatedTokens = conversation.EstimateTokenCount();
        var contextWindow = _config.Context.ContextWindowSize;
        var usagePercent = contextWindow > 0 ? (float)estimatedTokens / contextWindow * 100 : 0;
        var counts = conversation.GetMessageCountByRole();

        var usageColor = usagePercent switch
        {
            >= 90 => "red",
            >= 70 => "yellow",
            _ => "green"
        };

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .Title("[bold]Context Window[/]")
            .AddColumn("[bold]Metric[/]")
            .AddColumn("[bold]Value[/]");

        table.AddRow("Estimated tokens", $"[{usageColor}]{estimatedTokens:N0}[/]");
        table.AddRow("Context window", $"{contextWindow:N0}");
        table.AddRow("Usage", $"[{usageColor}]{usagePercent:F1}%[/]");
        table.AddRow("Auto-compact threshold", $"{_config.Context.AutoCompactThreshold * 100:F0}% ({_config.Context.AutoCompactEnabled switch { true => "[green]enabled[/]", false => "[grey]disabled[/]" }})");

        var systemTokens = (conversation.SystemPrompt?.Length ?? 0) / 4;
        table.AddRow("System prompt tokens", $"~{systemTokens:N0}");

        table.AddRow("", "");
        table.AddRow("[yellow]Messages[/]", "");
        foreach (var (role, count) in counts.OrderBy(k => k.Key))
            table.AddRow($"  {role}", count.ToString());
        table.AddRow("  total", conversation.Messages.Count.ToString());

        AnsiConsole.Write(table);

        // Visual bar
        var barWidth = 40;
        var filledWidth = (int)(usagePercent / 100 * barWidth);
        filledWidth = Math.Clamp(filledWidth, 0, barWidth);
        var bar = new string('#', filledWidth) + new string('-', barWidth - filledWidth);
        AnsiConsole.MarkupLine($"  [{usageColor}]{Markup.Escape($"[{bar}]")}[/]");
    }

    private void ShowStats(Conversation conversation)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .Title("[bold]Session Stats[/]")
            .AddColumn("[bold]Metric[/]")
            .AddColumn("[bold]Value[/]");

        table.AddRow("Session duration", _state.SessionStopwatch.Elapsed.ToString(@"hh\:mm\:ss"));
        table.AddRow("Total turns", _state.TotalTurns.ToString());
        table.AddRow("Output tokens", $"{_state.TotalOutputTokens:N0}");
        table.AddRow("Messages in context", conversation.Messages.Count.ToString());
        table.AddRow("Context tokens", $"~{conversation.EstimateTokenCount():N0}");

        if (_state.TotalTurns > 0)
            table.AddRow("Avg tokens/turn", $"{_state.TotalOutputTokens / _state.TotalTurns:N0}");

        AnsiConsole.Write(table);
    }

    private async Task HandleMemoryCommandAsync(string[] args)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "show";
        var loader = new ProjectInstructionsLoader();
        var cwd = Directory.GetCurrentDirectory();

        switch (action)
        {
            case "show":
                var content = await loader.LoadAsync(cwd);
                if (content is not null)
                {
                    AnsiConsole.Write(new Panel(Markup.Escape(content))
                        .Header("[cyan]ORCA.md[/]")
                        .Border(BoxBorder.Rounded)
                        .BorderColor(Color.Cyan1));
                }
                else
                {
                    var path = loader.GetInstructionsPath(cwd);
                    AnsiConsole.MarkupLine($"[yellow]No ORCA.md found. Create one at: {Markup.Escape(path ?? cwd)}[/]");
                }
                break;

            case "edit":
                var filePath = loader.GetInstructionsPath(cwd);
                if (filePath is null)
                {
                    // Create default path
                    var root = loader.FindProjectRoot(cwd) ?? cwd;
                    filePath = Path.Combine(root, "ORCA.md");
                    if (!File.Exists(filePath))
                        await File.WriteAllTextAsync(filePath, "# Project Instructions\n\n<!-- Add project-specific instructions for OpenOrca here -->\n");
                }

                var editor = Environment.GetEnvironmentVariable("EDITOR")
                    ?? (OperatingSystem.IsWindows() ? "notepad" : "nano");
                try
                {
                    var psi = new ProcessStartInfo(editor, filePath)
                    {
                        UseShellExecute = true
                    };
                    var proc = Process.Start(psi);
                    proc?.WaitForExit();
                    AnsiConsole.MarkupLine("[green]ORCA.md updated.[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to open editor: {Markup.Escape(ex.Message)}[/]");
                }
                break;

            default:
                AnsiConsole.MarkupLine("[yellow]Usage: /memory [show|edit][/]");
                break;
        }
    }

    private async Task RunDoctorAsync(CancellationToken ct)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .Title("[bold]OpenOrca Doctor[/]")
            .AddColumn("[bold]Check[/]")
            .AddColumn("[bold]Status[/]");

        // 1. LM Studio connection
        try
        {
            var discovery = new Core.Client.ModelDiscovery(_config,
                _logger as ILogger<Core.Client.ModelDiscovery>
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Client.ModelDiscovery>.Instance);
            var models = await discovery.GetAvailableModelsAsync(ct);
            if (models.Count > 0)
                table.AddRow("LM Studio connection", $"[green]Connected ({models.Count} model(s))[/]");
            else
                table.AddRow("LM Studio connection", "[yellow]Connected but no models loaded[/]");
        }
        catch (Exception)
        {
            table.AddRow("LM Studio connection", "[red]Unreachable[/]");
        }

        // 2. Model configured
        table.AddRow("Model configured",
            _config.LmStudio.Model is not null
                ? $"[green]{Markup.Escape(_config.LmStudio.Model)}[/]"
                : "[yellow]Auto-detect[/]");

        // 3. Tools registered
        var toolCount = Tools?.Count ?? 0;
        table.AddRow("Tools registered",
            toolCount > 0 ? $"[green]{toolCount} tools[/]" : "[red]No tools[/]");

        // 4. Config file exists
        var configPath = Path.Combine(ConfigManager.GetConfigDirectory(), "config.json");
        table.AddRow("Config file",
            File.Exists(configPath) ? "[green]Found[/]" : "[yellow]Using defaults[/]");

        // 5. Log directory writable
        var logDir = Path.Combine(ConfigManager.GetConfigDirectory(), "logs");
        try
        {
            Directory.CreateDirectory(logDir);
            table.AddRow("Log directory", "[green]Writable[/]");
        }
        catch (IOException)
        {
            table.AddRow("Log directory", "[red]Not writable[/]");
        }
        catch (UnauthorizedAccessException)
        {
            table.AddRow("Log directory", "[red]Not writable[/]");
        }

        // 6. Session storage accessible
        try
        {
            var sessions = _sessionManager.List();
            table.AddRow("Session storage", $"[green]Accessible ({sessions.Count} sessions)[/]");
        }
        catch (Exception)
        {
            table.AddRow("Session storage", "[red]Not accessible[/]");
        }

        // 7. Default prompt template
        var promptDir = Path.Combine(ConfigManager.GetConfigDirectory(), "prompts");
        var defaultPrompt = Path.Combine(promptDir, "default.md");
        table.AddRow("Default prompt template",
            File.Exists(defaultPrompt) ? "[green]Found[/]" : "[yellow]Missing[/]");

        // 8. Project instructions
        var loader = new ProjectInstructionsLoader();
        var orcaMd = await loader.LoadAsync(Directory.GetCurrentDirectory());
        table.AddRow("Project instructions (ORCA.md)",
            orcaMd is not null ? "[green]Found[/]" : "[grey]Not found[/]");

        // 9. Native tool calling
        table.AddRow("Native tool calling",
            _config.LmStudio.NativeToolCalling ? "[green]Enabled[/]" : "[grey]Disabled[/]");

        AnsiConsole.Write(table);
    }

    private async Task HandleModelCommandAsync(string[] args, CancellationToken ct)
    {
        if (args.Length > 0)
        {
            _config.LmStudio.Model = args[0];
            AnsiConsole.MarkupLine($"[green]Model set to: {Markup.Escape(args[0])}[/]");
        }
        else
        {
            var discovery = new Core.Client.ModelDiscovery(_config, _logger as ILogger<Core.Client.ModelDiscovery>
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Client.ModelDiscovery>.Instance);
            var models = await discovery.GetAvailableModelsAsync(ct);

            if (models.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No models found. Is LM Studio running?[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[bold]Available models:[/]");
                foreach (var m in models)
                    AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(m)}[/]");

                if (_config.LmStudio.Model is not null)
                    AnsiConsole.MarkupLine($"\n[grey]Current: {Markup.Escape(_config.LmStudio.Model)}[/]");
            }
        }
    }

    private void HandleCopy()
    {
        if (string.IsNullOrEmpty(_state.LastAssistantResponse))
        {
            AnsiConsole.MarkupLine("[yellow]No assistant response to copy.[/]");
            return;
        }

        // Strip <think> tags
        var cleaned = Regex.Replace(_state.LastAssistantResponse, @"<think>.*?</think>", "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();

        try
        {
            string clipCommand;
            string clipArgs;

            if (OperatingSystem.IsWindows())
            {
                clipCommand = "cmd.exe";
                clipArgs = "/c clip";
            }
            else if (OperatingSystem.IsMacOS())
            {
                clipCommand = "pbcopy";
                clipArgs = "";
            }
            else
            {
                clipCommand = "xclip";
                clipArgs = "-selection clipboard";
            }

            var psi = new ProcessStartInfo(clipCommand, clipArgs)
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                proc.StandardInput.Write(cleaned);
                proc.StandardInput.Close();
                proc.WaitForExit(5000);
            }

            AnsiConsole.MarkupLine($"[green]Copied {cleaned.Length} chars to clipboard.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to copy to clipboard: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private void HandleExport(string[] args, Conversation conversation)
    {
        var path = args.Length > 0
            ? args[0]
            : $"openorca-export-{DateTime.Now:yyyyMMdd-HHmmss}.md";

        try
        {
            var lines = new List<string>();
            lines.Add("# OpenOrca Conversation Export");
            lines.Add($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add("");

            if (conversation.SystemPrompt is not null)
            {
                lines.Add("## System Prompt");
                var sp = conversation.SystemPrompt;
                if (sp.Length > 500)
                    sp = sp[..500] + "\n... (truncated)";
                lines.Add("```");
                lines.Add(sp);
                lines.Add("```");
                lines.Add("");
            }

            foreach (var msg in conversation.Messages)
            {
                var role = msg.Role.Value;
                lines.Add($"## {char.ToUpper(role[0])}{role[1..]}");

                var text = msg.Text ?? string.Join("", msg.Contents.OfType<TextContent>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(text))
                    lines.Add(text);

                var funcCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
                foreach (var fc in funcCalls)
                {
                    lines.Add($"**Tool call:** `{fc.Name}`");
                    if (fc.Arguments is not null)
                    {
                        lines.Add("```json");
                        lines.Add(JsonSerializer.Serialize(fc.Arguments, new JsonSerializerOptions { WriteIndented = true }));
                        lines.Add("```");
                    }
                }

                var funcResults = msg.Contents.OfType<FunctionResultContent>().ToList();
                foreach (var fr in funcResults)
                {
                    lines.Add("**Tool result:**");
                    lines.Add("```");
                    var resultText = fr.Result?.ToString() ?? "(no result)";
                    if (resultText.Length > 1000)
                        resultText = resultText[..1000] + "\n... (truncated)";
                    lines.Add(resultText);
                    lines.Add("```");
                }

                lines.Add("");
            }

            File.WriteAllText(path, string.Join("\n", lines));
            AnsiConsole.MarkupLine($"[green]Exported conversation to {Markup.Escape(path)} ({conversation.Messages.Count} messages)[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to export: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private void ShowHelp()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .AddColumn("Command")
            .AddColumn("Description");

        table.AddRow(Markup.Escape("/help, /h, /?"), "Show this help");
        table.AddRow(Markup.Escape("/clear, /c"), "Clear conversation");
        table.AddRow(Markup.Escape("/model [name]"), "List or set model");
        table.AddRow(Markup.Escape("/config"), "Show configuration");
        table.AddRow(Markup.Escape("/session list|save|load"), "Manage sessions");
        table.AddRow(Markup.Escape("/plan, /p [on|off]"), "Toggle plan mode (plan before executing)");
        table.AddRow(Markup.Escape("/compact [instructions]"), "Compact conversation context with summary");
        table.AddRow(Markup.Escape("/rewind [N]"), "Remove last N turn(s) from conversation");
        table.AddRow(Markup.Escape("/context, /ctx"), "Show context window usage");
        table.AddRow(Markup.Escape("/stats, /cost"), "Show session statistics");
        table.AddRow(Markup.Escape("/memory [show|edit]"), "View or edit ORCA.md project instructions");
        table.AddRow(Markup.Escape("/doctor, /diag"), "Run diagnostic checks");
        table.AddRow(Markup.Escape("/copy, /cp"), "Copy last response to clipboard");
        table.AddRow(Markup.Escape("/export [path]"), "Export conversation to markdown");
        table.AddRow(Markup.Escape("!<command>"), "Run shell command directly");
        table.AddRow(Markup.Escape("/exit, /quit, /q"), "Exit OpenOrca");

        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine($"[grey]  Ctrl+O  Toggle thinking output (currently {(_state.ShowThinking ? "[green]visible[/]" : "[yellow]hidden[/]")})[/]");
        AnsiConsole.MarkupLine($"[grey]  Plan    {(_state.PlanMode ? "[cyan]active[/]" : "[grey]off[/]")} — use /plan to toggle[/]");
    }

    private static void ShowWelcomeBannerMinimal()
    {
        AnsiConsole.MarkupLine("[cyan bold]OpenOrca[/] [grey]— Type /help for commands[/]\n");
    }
}
