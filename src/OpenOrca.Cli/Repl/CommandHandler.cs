using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Cli.CustomCommands;
using OpenOrca.Cli.Rendering;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Orchestration;
using OpenOrca.Core.Serialization;
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
    private readonly TerminalPanel _panel;
    private readonly ReplState _state;
    private readonly ILogger _logger;
    private readonly CheckpointManager _checkpointManager;
    private readonly CustomCommandLoader _customCommandLoader;
    private readonly MemoryManager _memoryManager;
    private readonly AgentOrchestrator _orchestrator;

    public IList<AITool>? Tools { get; set; }

    /// <summary>
    /// Delegate to run the agent loop on a conversation. Set by ReplLoop to break circular dependency.
    /// </summary>
    public Func<Conversation, CancellationToken, Task>? RunAgentLoop { get; set; }

    /// <summary>
    /// Streaming renderer reference. Set by ReplLoop for benchmark mode suppression.
    /// </summary>
    public StreamingRenderer? StreamingRenderer { get; set; }

    public CommandHandler(
        IChatClient chatClient,
        OrcaConfig config,
        ConfigManager configManager,
        SessionManager sessionManager,
        ToolCallRenderer toolCallRenderer,
        ConversationManager conversationManager,
        SystemPromptBuilder systemPromptBuilder,
        ConfigEditor configEditor,
        TerminalPanel panel,
        ReplState state,
        ILogger logger,
        CheckpointManager checkpointManager,
        CustomCommandLoader customCommandLoader,
        MemoryManager memoryManager,
        AgentOrchestrator orchestrator)
    {
        _chatClient = chatClient;
        _config = config;
        _configManager = configManager;
        _sessionManager = sessionManager;
        _toolCallRenderer = toolCallRenderer;
        _conversationManager = conversationManager;
        _systemPromptBuilder = systemPromptBuilder;
        _configEditor = configEditor;
        _panel = panel;
        _state = state;
        _logger = logger;
        _checkpointManager = checkpointManager;
        _customCommandLoader = customCommandLoader;
        _memoryManager = memoryManager;
        _orchestrator = orchestrator;
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
                _panel.Teardown();
                AnsiConsole.Clear();
                _panel.Setup();
                ShowWelcomeBannerMinimal();
                return false;

            case SlashCommand.Model:
                await HandleModelCommandAsync(command.Args, ct);
                return false;

            case SlashCommand.Config:
                if (_config.DemoMode)
                    _configEditor.RenderConfigTable();
                else
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

            case SlashCommand.Init:
                await HandleInitAsync();
                return false;

            case SlashCommand.Diff:
                await HandleDiffAsync(ct);
                return false;

            case SlashCommand.Undo:
                await HandleUndoAsync(ct);
                return false;

            case SlashCommand.Rename:
                await HandleRenameAsync(command.Args, conversation);
                return false;

            case SlashCommand.Add:
                HandleAdd(command.Args, conversation);
                return false;

            case SlashCommand.Ask:
                HandleAskToggle();
                return false;

            case SlashCommand.Checkpoint:
                await HandleCheckpointAsync(command.Args);
                return false;

            case SlashCommand.Fork:
                await HandleForkAsync(command.Args, conversation);
                return false;

            case SlashCommand.Review:
                await HandleReviewAsync(command.Args, ct);
                return false;

            case SlashCommand.Benchmark:
                await HandleBenchmarkAsync(ct);
                return false;

            case SlashCommand.CustomCommand:
                await HandleCustomCommandAsync(command.Args, conversation);
                return false;

            default:
                AnsiConsole.MarkupLine("[yellow]Unknown command. Type /help for available commands.[/]");
                return false;
        }
    }

    public async Task ExecuteBashShortcutAsync(string command, CancellationToken ct = default)
    {
        _logger.LogInformation("Bash shortcut: {Command}", command);

        try
        {
            var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";

            var psi = new ProcessStartInfo(shell)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };

            string stdout = "";
            string stderr = "";
            int exitCode = -1;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("yellow"))
                .StartAsync($"Running: {command}", async _ =>
                {
                    using var proc = Process.Start(psi);
                    if (proc is null)
                        throw new InvalidOperationException("Failed to start shell process.");

                    // Write command via stdin to avoid shell argument escaping issues
                    if (OperatingSystem.IsWindows())
                        await proc.StandardInput.WriteLineAsync("@echo off");
                    await proc.StandardInput.WriteLineAsync(command);
                    proc.StandardInput.Close();

                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(CliConstants.BashShortcutTimeoutSeconds));
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                    stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
                    stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
                    await proc.WaitForExitAsync(cts.Token);
                    exitCode = proc.ExitCode;
                });

            // Truncate for display
            if (stdout.Length > CliConstants.BashOutputMaxChars)
                stdout = stdout[..CliConstants.BashOutputMaxChars] + "\n... (truncated)";
            if (stderr.Length > CliConstants.BashOutputMaxChars)
                stderr = stderr[..CliConstants.BashOutputMaxChars] + "\n... (truncated)";

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

            var exitColor = exitCode == 0 ? "green" : "red";
            AnsiConsole.MarkupLine($"[{exitColor}]Exit code: {exitCode}[/]");
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Command timed out (120s).[/]");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("shell process"))
        {
            AnsiConsole.MarkupLine("[red]Failed to start shell process.[/]");
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

        var tokensBefore = conversation.EstimateTokenCount(_config.Context.CharsPerToken);
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
                    $"[{msg.Role.Value}]: {(text.Length > CliConstants.LogTextMaxChars ? text[..CliConstants.LogTextMaxChars] + "..." : text)}"));
        }

        try
        {
            string summary = "";

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("yellow"))
                .StartAsync("Compacting conversation...", async _ =>
                {
                    var options = new ChatOptions
                    {
                        Temperature = 0.3f,
                        MaxOutputTokens = CliConstants.CompactMaxOutputTokens,
                    };
                    options.ModelId ??= _config.LmStudio.Model;

                    var response = await _chatClient.GetResponseAsync(summaryMessages, options, ct);
                    summary = string.Join("", response.Messages
                        .SelectMany(m => m.Contents.OfType<TextContent>())
                        .Select(t => t.Text));
                });

            if (string.IsNullOrWhiteSpace(summary))
            {
                AnsiConsole.MarkupLine("[red]Failed to generate summary — empty response.[/]");
                return;
            }

            var removed = conversation.CompactWithSummary(summary, preserveLastN);
            var tokensAfter = conversation.EstimateTokenCount(_config.Context.CharsPerToken);

            AnsiConsole.MarkupLine($"[green]Compacted: {msgCountBefore} messages -> {conversation.Messages.Count} messages (~{tokensBefore} -> ~{tokensAfter} tokens)[/]");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to compact conversation");
            AnsiConsole.MarkupLine($"[red]Failed to compact: {Markup.Escape(ex.Message)}[/]");
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
                string? id = null;
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("yellow"))
                    .StartAsync("Saving session...", async _ =>
                    {
                        id = await _sessionManager.SaveAsync(conversation, title, _state.CurrentSessionId);
                    });
                _state.CurrentSessionId = id!;
                AnsiConsole.MarkupLine($"[green]Session saved: {id}[/]");
                break;

            case "load":
                if (args.Length < 2)
                {
                    AnsiConsole.MarkupLine("[yellow]Usage: /session load <id>[/]");
                    return;
                }
                SessionData? sessionData = null;
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("yellow"))
                    .StartAsync("Loading session...", async _ =>
                    {
                        sessionData = await _sessionManager.LoadAsync(args[1]);
                    });
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

            case "tree":
                var tree = _sessionManager.GetSessionTree();
                if (tree.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]No saved sessions.[/]");
                    return;
                }

                var spectreTree = new Tree("[bold]Sessions[/]");
                var nodeStack = new Dictionary<int, TreeNode>();

                foreach (var (session, depth) in tree)
                {
                    var isCurrent = session.Id == _state.CurrentSessionId;
                    var label = isCurrent
                        ? $"[green bold]{Markup.Escape(session.Id)}[/] [green]{Markup.Escape(session.Title)}[/] [grey]({session.Messages.Count} msgs)[/]"
                        : $"[cyan]{Markup.Escape(session.Id)}[/] {Markup.Escape(session.Title)} [grey]({session.Messages.Count} msgs)[/]";

                    if (session.ParentSessionId is not null)
                        label += $" [dim]← fork of {Markup.Escape(session.ParentSessionId)}[/]";

                    TreeNode node;
                    if (depth == 0)
                    {
                        node = spectreTree.AddNode(label);
                    }
                    else if (nodeStack.TryGetValue(depth - 1, out var parent))
                    {
                        node = parent.AddNode(label);
                    }
                    else
                    {
                        node = spectreTree.AddNode(label);
                    }

                    nodeStack[depth] = node;
                }

                AnsiConsole.Write(spectreTree);
                break;

            default:
                AnsiConsole.MarkupLine("[yellow]Usage: /session list|save [name]|load <id>|delete <id>|tree[/]");
                break;
        }
    }

    private void HandleAskToggle()
    {
        _state.Mode = _state.Mode == InputMode.Ask ? InputMode.Normal : InputMode.Ask;
        var enabled = _state.Mode == InputMode.Ask;

        if (enabled)
            AnsiConsole.MarkupLine("[magenta]Ask mode enabled[/] — responses will not use tools.");
        else
            AnsiConsole.MarkupLine("[grey]Ask mode disabled[/] — back to normal mode.");

        _logger.LogInformation("Ask mode toggled: {Enabled}", enabled);
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
        var estimatedTokens = conversation.EstimateTokenCount(_config.Context.CharsPerToken);
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
        table.AddRow("Context tokens", $"~{conversation.EstimateTokenCount(_config.Context.CharsPerToken):N0}");

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

                // Also show auto memory
                var autoMemory = await _memoryManager.LoadAllMemoryAsync();
                if (!string.IsNullOrWhiteSpace(autoMemory))
                {
                    AnsiConsole.Write(new Panel(Markup.Escape(autoMemory))
                        .Header("[magenta]Auto Memory[/]")
                        .Border(BoxBorder.Rounded)
                        .BorderColor(Color.Magenta1));
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
                    using var proc = Process.Start(psi);
                    if (proc is null)
                    {
                        AnsiConsole.MarkupLine("[red]Failed to launch editor.[/]");
                        break;
                    }
                    proc.WaitForExit();
                    AnsiConsole.MarkupLine("[green]ORCA.md updated.[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to open editor: {Markup.Escape(ex.Message)}[/]");
                }
                break;

            case "auto":
                if (args.Length > 1)
                {
                    var toggle = args[1].ToLowerInvariant();
                    _config.Memory.AutoMemoryEnabled = toggle is "on" or "true" or "1";
                    AnsiConsole.MarkupLine(_config.Memory.AutoMemoryEnabled
                        ? "[green]Auto memory enabled.[/]"
                        : "[grey]Auto memory disabled.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"Auto memory is [{(_config.Memory.AutoMemoryEnabled ? "green" : "grey")}]{(_config.Memory.AutoMemoryEnabled ? "enabled" : "disabled")}[/].");
                }
                break;

            case "list":
                var files = await _memoryManager.ListAsync();
                if (files.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]No auto memory files.[/]");
                }
                else
                {
                    foreach (var (memPath, preview) in files)
                    {
                        var name = Path.GetFileName(memPath);
                        AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(name)}[/] — {Markup.Escape(preview)}");
                    }
                }
                break;

            case "clear-auto":
                await _memoryManager.ClearAsync();
                AnsiConsole.MarkupLine("[green]Auto memory files cleared.[/]");
                break;

            default:
                AnsiConsole.MarkupLine("[yellow]Usage: /memory [show|edit|auto on/off|list|clear-auto][/]");
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

        // 10. Sandbox mode
        table.AddRow("Sandbox mode",
            _config.SandboxMode ? "[yellow]Enabled (read-only tools only)[/]" : "[grey]Disabled[/]");

        // 11. Directory restriction
        table.AddRow("Directory restriction",
            _config.AllowedDirectory is not null
                ? $"[yellow]{Markup.Escape(_config.AllowedDirectory)}[/]"
                : "[grey]None[/]");

        // 12. Thinking budget
        table.AddRow("Thinking budget",
            _config.Thinking.BudgetTokens > 0
                ? $"[cyan]{_config.Thinking.BudgetTokens} tokens[/]"
                : "[grey]Unlimited[/]");

        // 13. MCP servers
        var enabledMcp = _config.McpServers.Count(kv => kv.Value.Enabled);
        table.AddRow("MCP servers",
            _config.McpServers.Count > 0
                ? $"[cyan]{enabledMcp}/{_config.McpServers.Count} enabled[/]"
                : "[grey]None configured[/]");

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
                proc.WaitForExit(CliConstants.ClipboardProcessWaitMs);
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
                if (sp.Length > CliConstants.SystemPromptDisplayMaxChars)
                    sp = sp[..CliConstants.SystemPromptDisplayMaxChars] + "\n... (truncated)";
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
                        lines.Add(JsonSerializer.Serialize(fc.Arguments, OrcaJsonContext.Default.IDictionaryStringObject));
                        lines.Add("```");
                    }
                }

                var funcResults = msg.Contents.OfType<FunctionResultContent>().ToList();
                foreach (var fr in funcResults)
                {
                    lines.Add("**Tool result:**");
                    lines.Add("```");
                    var resultText = fr.Result?.ToString() ?? "(no result)";
                    if (resultText.Length > CliConstants.ToolResultLogMaxChars)
                        resultText = resultText[..CliConstants.ToolResultLogMaxChars] + "\n... (truncated)";
                    lines.Add(resultText);
                    lines.Add("```");
                }

                lines.Add("");
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, string.Join("\n", lines));
            AnsiConsole.MarkupLine($"[green]Exported conversation to {Markup.Escape(path)} ({conversation.Messages.Count} messages)[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to export: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private async Task HandleInitAsync()
    {
        var loader = new ProjectInstructionsLoader();
        var cwd = Directory.GetCurrentDirectory();
        var root = loader.FindProjectRoot(cwd) ?? cwd;
        var orcaDir = Path.Combine(root, ".orca");
        var orcaPath = Path.Combine(orcaDir, "ORCA.md");

        // Check if instructions already exist
        var existing = loader.GetInstructionsPath(cwd);
        if (existing is not null && File.Exists(existing))
        {
            AnsiConsole.MarkupLine($"[yellow]Project instructions already exist at: {Markup.Escape(existing)}[/]");
            AnsiConsole.MarkupLine("[grey]Use /memory edit to modify them.[/]");
            return;
        }

        Directory.CreateDirectory(orcaDir);
        await File.WriteAllTextAsync(orcaPath, """
            # Project Instructions

            ## Overview
            <!-- Brief description of the project -->

            ## Architecture
            <!-- Key directories, patterns, frameworks -->

            ## Code Style
            <!-- Naming conventions, formatting, preferred patterns -->

            ## Testing
            <!-- How to run tests, testing conventions -->

            ## Common Commands
            <!-- Build, test, deploy commands -->
            """.Replace("            ", ""));

        AnsiConsole.MarkupLine($"[green]Created project instructions at: {Markup.Escape(orcaPath)}[/]");
        AnsiConsole.MarkupLine("[grey]Edit with /memory edit or open the file directly.[/]");
    }

    private async Task HandleDiffAsync(CancellationToken ct)
    {
        try
        {
            var staged = await RunGitCommandAsync("git diff --cached", ct);
            var stagedStat = await RunGitCommandAsync("git diff --cached --stat", ct);
            var unstaged = await RunGitCommandAsync("git diff", ct);
            var unstagedStat = await RunGitCommandAsync("git diff --stat", ct);

            if (string.IsNullOrWhiteSpace(staged) && string.IsNullOrWhiteSpace(unstaged))
            {
                AnsiConsole.MarkupLine("[grey]No uncommitted changes.[/]");
                return;
            }

            if (!string.IsNullOrWhiteSpace(staged))
            {
                var display = stagedStat + "\n\n" + staged;
                DiffRenderer.RenderUnifiedDiff(display.TrimEnd(), "Staged Changes", Color.Green);
            }

            if (!string.IsNullOrWhiteSpace(unstaged))
            {
                var display = unstagedStat + "\n\n" + unstaged;
                DiffRenderer.RenderUnifiedDiff(display.TrimEnd(), "Unstaged Changes", Color.Yellow);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to get diff: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private async Task HandleUndoAsync(CancellationToken ct)
    {
        try
        {
            var stat = await RunGitCommandAsync("git diff --stat", ct);
            var stagedStat = await RunGitCommandAsync("git diff --cached --stat", ct);
            var combined = (stagedStat + "\n" + stat).Trim();

            if (string.IsNullOrWhiteSpace(combined))
            {
                AnsiConsole.MarkupLine("[grey]No changes to undo.[/]");
                return;
            }

            AnsiConsole.Write(new Panel(Markup.Escape(combined))
                .Header("[yellow]Changes to undo[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Yellow));

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]How would you like to undo these changes?[/]")
                    .AddChoices("Revert all (git checkout . && git reset HEAD)", "Stash changes (git stash)", "Cancel"));

            switch (choice)
            {
                case "Revert all (git checkout . && git reset HEAD)":
                    await RunGitCommandAsync("git reset HEAD", ct);
                    await RunGitCommandAsync("git checkout .", ct);
                    AnsiConsole.MarkupLine("[green]All changes reverted.[/]");
                    break;
                case "Stash changes (git stash)":
                    var stashResult = await RunGitCommandAsync("git stash", ct);
                    AnsiConsole.MarkupLine($"[green]{Markup.Escape(stashResult.Trim())}[/]");
                    break;
                default:
                    AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
                    break;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to undo: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private async Task HandleRenameAsync(string[] args, Conversation conversation)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Usage: /rename <new name>[/]");
            return;
        }

        if (_state.CurrentSessionId is null)
        {
            AnsiConsole.MarkupLine("[yellow]No active session. Save first with /session save.[/]");
            return;
        }

        var newTitle = string.Join(" ", args);
        _state.CurrentSessionId = await _sessionManager.SaveAsync(conversation, newTitle, _state.CurrentSessionId);
        AnsiConsole.MarkupLine($"[green]Session renamed to: {Markup.Escape(newTitle)}[/]");
    }

    private void HandleAdd(string[] args, Conversation conversation)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Usage: /add <file1> [file2] ...[/]");
            return;
        }

        var addedFiles = new List<string>();
        var contentParts = new List<string>();

        foreach (var pattern in args)
        {
            IEnumerable<string> files;
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                var dir = Path.GetDirectoryName(pattern);
                var searchPattern = Path.GetFileName(pattern);
                dir = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(dir);
                files = Directory.Exists(dir)
                    ? Directory.GetFiles(dir, searchPattern, SearchOption.TopDirectoryOnly)
                    : [];
            }
            else
            {
                var fullPath = Path.GetFullPath(pattern);
                files = File.Exists(fullPath) ? [fullPath] : [];
            }

            foreach (var file in files)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    const int maxChars = 50_000;
                    if (content.Length > maxChars)
                        content = content[..maxChars] + "\n... (truncated)";

                    var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file);
                    contentParts.Add($"--- {relativePath} ---\n{content}");
                    addedFiles.Add(relativePath);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to read {Markup.Escape(pattern)}: {Markup.Escape(ex.Message)}[/]");
                }
            }

            if (!files.Any())
                AnsiConsole.MarkupLine($"[yellow]No files matched: {Markup.Escape(pattern)}[/]");
        }

        if (contentParts.Count > 0)
        {
            var message = "Here are the contents of the requested files:\n\n" + string.Join("\n\n", contentParts);
            conversation.AddUserMessage(message);
            AnsiConsole.MarkupLine($"[green]Added {addedFiles.Count} file(s) to context: {Markup.Escape(string.Join(", ", addedFiles))}[/]");
        }
    }

    private async Task<string> RunGitCommandAsync(string command, CancellationToken ct)
    {
        var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";
        var shellArg = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"";

        var psi = new ProcessStartInfo(shell, shellArg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return stdout;
    }

    private async Task HandleCheckpointAsync(string[] args)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
        var sessionId = _state.CurrentSessionId ?? "default";

        switch (action)
        {
            case "list":
                var entries = await _checkpointManager.ListAsync(sessionId);
                if (entries.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]No file checkpoints in this session.[/]");
                    return;
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Expand()
                    .AddColumn("File")
                    .AddColumn("Checkpointed At")
                    .AddColumn("Size");

                foreach (var entry in entries)
                {
                    var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), entry.FilePath);
                    table.AddRow(
                        Markup.Escape(relativePath),
                        entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                        $"{entry.SizeBytes:N0} bytes");
                }
                AnsiConsole.Write(table);
                break;

            case "diff":
                var diffPath = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
                if (diffPath is null)
                {
                    AnsiConsole.MarkupLine("[yellow]Usage: /checkpoint diff <file>[/]");
                    return;
                }
                var diff = await _checkpointManager.DiffAsync(diffPath, sessionId);
                DiffRenderer.RenderUnifiedDiff(diff, "Checkpoint Diff", Color.Cyan1);
                break;

            case "restore":
                var restorePath = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
                if (restorePath is null)
                {
                    AnsiConsole.MarkupLine("[yellow]Usage: /checkpoint restore <file>[/]");
                    return;
                }
                var confirm = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[yellow]Restore {Markup.Escape(Path.GetFileName(restorePath))} from checkpoint?[/]")
                        .AddChoices("Yes", "No"));
                if (confirm == "Yes")
                {
                    var restored = await _checkpointManager.RestoreAsync(restorePath, sessionId);
                    if (restored)
                        AnsiConsole.MarkupLine($"[green]Restored: {Markup.Escape(restorePath)}[/]");
                    else
                        AnsiConsole.MarkupLine($"[red]No checkpoint found for: {Markup.Escape(restorePath)}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
                }
                break;

            case "clear":
                _checkpointManager.Cleanup(sessionId);
                AnsiConsole.MarkupLine("[green]All checkpoints cleared for this session.[/]");
                break;

            default:
                AnsiConsole.MarkupLine("[yellow]Usage: /checkpoint list|diff <file>|restore <file>|clear[/]");
                break;
        }
    }

    private async Task HandleForkAsync(string[] args, Conversation conversation)
    {
        var parentId = _state.CurrentSessionId;
        if (parentId is null)
        {
            // Save current session first to establish a parent
            try
            {
                parentId = await _sessionManager.SaveAsync(conversation);
                _state.CurrentSessionId = parentId;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to save current session before forking: {Markup.Escape(ex.Message)}[/]");
                return;
            }
        }

        var title = args.Length > 0 ? string.Join(" ", args) : null;
        var messageIndex = conversation.Messages.Count;

        try
        {
            var newId = await _sessionManager.ForkAsync(conversation, title, parentId, messageIndex);
            AnsiConsole.MarkupLine($"[green]Forked as session {newId}: {Markup.Escape(title ?? $"Fork of {parentId}")}[/]");
            AnsiConsole.MarkupLine("[grey]Current session continues unaffected. Use /session load to switch.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to fork session: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private async Task HandleCustomCommandAsync(string[] args, Conversation conversation)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Custom command name missing.[/]");
            return;
        }

        var commandName = args[0];
        var userArgs = args.Length > 1 ? args[1..] : [];

        try
        {
            var template = await _customCommandLoader.LoadTemplateAsync(commandName);
            if (template is null)
            {
                AnsiConsole.MarkupLine($"[red]Custom command template not found: {Markup.Escape(commandName)}[/]");
                return;
            }

            // Substitute template variables
            var joined = string.Join(" ", userArgs);
            var rendered = template.Replace("{{ARGS}}", joined);
            for (var i = 0; i < userArgs.Length; i++)
            {
                rendered = rendered.Replace($"{{{{ARG{i + 1}}}}}", userArgs[i]);
            }

            // Inject as user message
            conversation.AddUserMessage(rendered);
            AnsiConsole.MarkupLine($"[cyan]Running custom command: /{Markup.Escape(commandName)}[/]");
            _logger.LogInformation("Custom command /{Name} injected ({Len} chars)", commandName, rendered.Length);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to load custom command: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private async Task HandleBenchmarkAsync(CancellationToken ct)
    {
        if (RunAgentLoop is null)
        {
            AnsiConsole.MarkupLine("[red]Benchmark is not available (agent loop not wired).[/]");
            return;
        }

        var runner = new BenchmarkRunner(_config, _systemPromptBuilder, _logger, RunAgentLoop,
            _toolCallRenderer, StreamingRenderer!, _state);

        try
        {
            await runner.RunAsync(ct);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Benchmark cancelled.[/]");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Benchmark failed");
            AnsiConsole.MarkupLine($"[red]Benchmark failed: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private async Task HandleReviewAsync(string[] args, CancellationToken ct)
    {
        string task;
        if (args.Length == 0)
        {
            task = "Review the uncommitted changes in this git repository. " +
                   "Use git_status and git_diff to understand what changed, then examine the modified files for bugs, quality issues, and suggestions.";
        }
        else if (args[0].Equals("staged", StringComparison.OrdinalIgnoreCase))
        {
            task = "Review the staged changes in this git repository. " +
                   "Use git_diff with the staged option to see only staged changes, then examine the modified files for bugs, quality issues, and suggestions.";
        }
        else if (args[0].Contains('.') || args[0].Contains('/') || args[0].Contains('\\'))
        {
            // Looks like a file path
            var filePath = string.Join(" ", args);
            task = $"Review the code in {filePath} for quality, bugs, and suggestions.";
        }
        else
        {
            // Assume it's a commit hash or ref
            var commitRef = args[0];
            task = $"Review the changes introduced by commit {commitRef}. " +
                   "Use git_diff and git_log to understand the changes, then examine the modified files for bugs, quality issues, and suggestions.";
        }

        AnsiConsole.MarkupLine($"[yellow]Spawning review agent...[/]");

        try
        {
            var context = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("yellow"))
                .StartAsync("Review agent working...", async _ =>
                    await _orchestrator.SpawnAgentAsync(task, "review", ct));

            if (context.Status == AgentStatus.Completed)
            {
                AnsiConsole.MarkupLine($"[green]Review completed ({context.IterationCount} iterations)[/]");
                if (!string.IsNullOrWhiteSpace(context.Result))
                {
                    AnsiConsole.Write(new Panel(Markup.Escape(context.Result.TrimEnd()))
                        .Header("[green]Code Review[/]")
                        .Border(BoxBorder.Rounded)
                        .BorderColor(Color.Green));
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Review agent {context.Status}: {Markup.Escape(context.Error ?? "")}[/]");
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Review cancelled.[/]");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Review command failed");
            AnsiConsole.MarkupLine($"[red]Review failed: {Markup.Escape(ex.Message)}[/]");
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
        table.AddRow(Markup.Escape("/memory [show|edit|auto|list|clear-auto]"), "View/edit ORCA.md and auto memory");
        table.AddRow(Markup.Escape("/doctor, /diag"), "Run diagnostic checks");
        table.AddRow(Markup.Escape("/copy, /cp"), "Copy last response to clipboard");
        table.AddRow(Markup.Escape("/export [path]"), "Export conversation to markdown");
        table.AddRow(Markup.Escape("/init"), "Scaffold .orca/ORCA.md project instructions");
        table.AddRow(Markup.Escape("/diff"), "Show uncommitted git changes");
        table.AddRow(Markup.Escape("/undo"), "Revert or stash uncommitted changes");
        table.AddRow(Markup.Escape("/rename <name>"), "Rename current session");
        table.AddRow(Markup.Escape("/add <file> [...]"), "Add file contents to conversation context");
        table.AddRow(Markup.Escape("/ask [question]"), "Toggle ask mode (no args) or one-shot ask (with args)");
        table.AddRow(Markup.Escape("/checkpoint list|diff|restore|clear"), "Manage file checkpoints (auto-saved before edits)");
        table.AddRow(Markup.Escape("/fork [name]"), "Fork current session (creates a branch)");
        table.AddRow(Markup.Escape("/review [staged|commit|file]"), "Run code review via sub-agent");
        table.AddRow(Markup.Escape("/benchmark, /bench"), "Benchmark all loaded models with a coding task");
        table.AddRow(Markup.Escape("!<command>"), "Run shell command directly");
        table.AddRow(Markup.Escape("/exit, /quit, /q"), "Exit OpenOrca");

        AnsiConsole.Write(table);

        // Show discovered custom commands
        var customCommands = _customCommandLoader.DiscoverCommands();
        if (customCommands.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold]Custom Commands[/] [grey](from .orca/commands/)[/]");
            foreach (var (name, path) in customCommands)
            {
                var firstLine = "";
                try
                {
                    using var reader = new StreamReader(path);
                    firstLine = reader.ReadLine()?.TrimStart('#', ' ') ?? "";
                    if (firstLine.Length > 60) firstLine = firstLine[..57] + "...";
                }
                catch { /* best effort file preview */ }
                AnsiConsole.MarkupLine($"  [cyan]/{Markup.Escape(name)}[/] {Markup.Escape(firstLine)}");
            }
        }

        AnsiConsole.MarkupLine($"[grey]  Shift+Tab  Cycle input mode (Normal → Plan → Ask → Normal)[/]");
        AnsiConsole.MarkupLine($"[grey]  Ctrl+O     Toggle thinking output (currently {(_state.ShowThinking ? "[green]visible[/]" : "[yellow]hidden[/]")})[/]");
        AnsiConsole.MarkupLine($"[grey]  Mode       {(_state.Mode switch { InputMode.Plan => "[cyan]Plan[/]", InputMode.Ask => "[magenta]Ask[/]", _ => "[grey]Normal[/]" })}[/]");
        AnsiConsole.MarkupLine("[grey]  Tip: End a line with \\ to continue input on the next line[/]");
    }

    private static void ShowWelcomeBannerMinimal()
    {
        AnsiConsole.MarkupLine("[cyan bold]OpenOrca[/] [grey]— Type /help for commands[/]\n");
    }
}
