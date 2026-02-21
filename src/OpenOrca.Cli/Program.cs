using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using OpenOrca.Cli.Logging;
using OpenOrca.Cli.Rendering;
using OpenOrca.Cli.Repl;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Client;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Orchestration;
using OpenOrca.Core.Permissions;
using OpenOrca.Core.Session;
using OpenOrca.Core.Hooks;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Agent;
using OpenOrca.Tools.Interactive;
using OpenOrca.Tools.Registry;
using Spectre.Console;

// UTF-8 output encoding — prevents U+2022 (•) from encoding as 0x07 (BEL) in CP437
Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// Console logging: errors only. All debug/info goes to file.
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("OpenOrca", LogLevel.Debug);

// File logger: ~/.openorca/logs/openorca-{date}.log
var fileLoggerProvider = new FileLoggerProvider(LogLevel.Debug);
builder.Logging.AddProvider(fileLoggerProvider);

// Load config
var configManager = new ConfigManager();
await configManager.LoadAsync();
var config = configManager.Config;

// Parse --demo flag
var demoMode = Array.Exists(args, a => a == "--demo");
if (demoMode)
{
    config.DemoMode = true;
    config.LmStudio.NativeToolCalling = false;
    config.LmStudio.Model = "demo-model";
    config.Permissions.AutoApproveAll = true;
    config.Session.AutoSave = false;
    config.Context.AutoCompactEnabled = false;
}

// Parse --allow <tool1,tool2,...> flag
var allowArgIndex = Array.IndexOf(args, "--allow");
if (allowArgIndex >= 0 && allowArgIndex + 1 < args.Length)
{
    var toolNames = args[allowArgIndex + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var tool in toolNames)
        config.Permissions.AlwaysApprove.Add(tool);
}

// Parse --output json flag
var outputArgIndex = Array.IndexOf(args, "--output");
var jsonOutputMode = outputArgIndex >= 0 && outputArgIndex + 1 < args.Length
    && args[outputArgIndex + 1].Equals("json", StringComparison.OrdinalIgnoreCase);

// Parse --continue / -c and --resume / -r flags
var continueSession = Array.Exists(args, a => a is "--continue" or "-c");
var resumeArgIndex = Array.FindIndex(args, a => a is "--resume" or "-r");
string? resumeSessionId = resumeArgIndex >= 0 && resumeArgIndex + 1 < args.Length
    ? args[resumeArgIndex + 1] : null;

// Register config
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(configManager);

// Register core services
builder.Services.AddSingleton<LmStudioClientFactory>();
builder.Services.AddSingleton<ModelDiscovery>();
builder.Services.AddSingleton<ConversationManager>();
builder.Services.AddSingleton<PermissionManager>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<AgentOrchestrator>();
builder.Services.AddSingleton<PromptManager>();

// Register hooks
builder.Services.AddSingleton(config.Hooks);
builder.Services.AddSingleton<HookRunner>();

// Register shared REPL state (injected into InputHandler + ReplLoop)
builder.Services.AddSingleton<ReplState>();
builder.Services.AddSingleton<TerminalPanel>();

// Register CLI services
builder.Services.AddSingleton<InputHandler>();
builder.Services.AddSingleton<CommandParser>();
builder.Services.AddSingleton<StreamingRenderer>();
builder.Services.AddSingleton<ToolCallRenderer>();

// Register IChatClient — use embedded demo client or real LM Studio factory
if (demoMode)
{
    builder.Services.AddSingleton<IChatClient>(new DemoChatClient());
}
else
{
    builder.Services.AddSingleton<IChatClient>(sp =>
    {
        var factory = sp.GetRequiredService<LmStudioClientFactory>();
        return factory.Create();
    });
}

// Register tool registry
builder.Services.AddSingleton<ToolRegistry>();

// Register REPL
builder.Services.AddSingleton<ReplLoop>();

var host = builder.Build();

var programLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OpenOrca.Program");

// Ensure default prompt template exists
var promptManager = host.Services.GetRequiredService<PromptManager>();
await promptManager.EnsureDefaultPromptAsync();

// Discover and register tools
var toolRegistry = host.Services.GetRequiredService<ToolRegistry>();
toolRegistry.DiscoverTools(typeof(ToolRegistry).Assembly);

// Set up permission manager with interactive prompt
var permissionManager = host.Services.GetRequiredService<PermissionManager>();
var toolCallRenderer = host.Services.GetRequiredService<ToolCallRenderer>();

permissionManager.PromptForApproval = (toolName, riskLevel) =>
{
    toolCallRenderer.RenderPermissionPrompt(toolName, riskLevel);

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Allow this tool call?")
            .AddChoices("Yes", "Yes to all (this tool, this session)", "No"));

    return Task.FromResult(choice switch
    {
        "Yes" => PermissionDecision.Approved,
        "Yes to all (this tool, this session)" => PermissionDecision.ApproveAll,
        _ => PermissionDecision.Denied
    });
};

var hookRunner = host.Services.GetRequiredService<HookRunner>();

// Create tool executor with permission checks
async Task<string> ExecuteToolAsync(string toolName, string argsJson, CancellationToken ct)
{
    programLogger.LogDebug("ExecuteToolAsync called: {Tool} args: {Args}",
        toolName, argsJson.Length > 200 ? argsJson[..200] + "..." : argsJson);

    var tool = toolRegistry.Resolve(toolName);
    if (tool is null)
    {
        programLogger.LogWarning("Unknown tool requested: {Tool}", toolName);
        return $"Unknown tool: {toolName}. This tool is not available. Use only the tools listed in your system prompt.";
    }

    programLogger.LogDebug("Tool resolved: {Tool} (risk: {Risk})", toolName, tool.RiskLevel);

    // Permission check
    var approved = await permissionManager.CheckPermissionAsync(toolName, tool.RiskLevel.ToString());
    if (!approved)
    {
        programLogger.LogInformation("Permission denied for tool: {Tool}", toolName);
        return "Permission denied by user.";
    }

    // Pre-tool hook
    if (!await hookRunner.RunPreHookAsync(toolName, argsJson, ct))
    {
        programLogger.LogInformation("Tool {Tool} blocked by pre-hook", toolName);
        return "Tool blocked by hook.";
    }

    JsonElement argsElement;
    try
    {
        argsElement = JsonDocument.Parse(argsJson).RootElement;
    }
    catch (Exception parseEx)
    {
        programLogger.LogWarning(parseEx, "Failed to parse tool args JSON, using empty object");
        argsElement = JsonDocument.Parse("{}").RootElement;
    }

    // Validate required arguments before execution
    var schema = tool.ParameterSchema;
    if (schema.TryGetProperty("required", out var requiredArr) && requiredArr.ValueKind == JsonValueKind.Array)
    {
        var missing = new List<string>();
        foreach (var req in requiredArr.EnumerateArray())
        {
            var paramName = req.GetString();
            if (paramName is not null && !argsElement.TryGetProperty(paramName, out _))
                missing.Add(paramName);
        }
        if (missing.Count > 0)
        {
            var missingList = string.Join(", ", missing);
            programLogger.LogWarning("Tool {Tool} called with missing required args: {Missing}", toolName, missingList);
            return $"ERROR: {toolName} was called with missing required arguments: {missingList}. " +
                   $"Expected schema: {schema.GetRawText()} " +
                   $"You provided: {argsElement.GetRawText()} " +
                   $"Try again with the correct arguments.";
        }
    }

    try
    {
        var result = await tool.ExecuteAsync(argsElement, ct);
        programLogger.LogDebug("Tool {Tool} completed: isError={IsError} ({Len} chars)",
            toolName, result.IsError, result.Content.Length);

        // Post-tool hook
        _ = hookRunner.RunPostHookAsync(toolName, argsJson, result.Content, result.IsError, ct);

        // Prefix errors so the LLM clearly sees the tool failed and can adapt
        // (e.g. "not a git repository" → model should init one or use a different tool)
        if (result.IsError)
            return $"ERROR: {result.Content}";

        return result.Content;
    }
    catch (Exception ex)
    {
        programLogger.LogError(ex, "Tool {Tool} execution threw unhandled exception", toolName);
        _ = hookRunner.RunPostHookAsync(toolName, argsJson, ex.Message, true, ct);
        return $"ERROR executing {toolName}: {ex.Message}";
    }
}

// Set up agent orchestrator
var orchestrator = host.Services.GetRequiredService<AgentOrchestrator>();
var aiTools = toolRegistry.GenerateAITools();
orchestrator.Tools = aiTools;
orchestrator.ToolExecutor = ExecuteToolAsync;

// Wire SpawnAgentTool to orchestrator
var spawnTool = toolRegistry.Resolve("spawn_agent") as SpawnAgentTool;
if (spawnTool is not null)
{
    spawnTool.AgentSpawner = async (task, agentType, ct) =>
    {
        AnsiConsole.MarkupLine($"[yellow]Spawning {Markup.Escape(agentType)} sub-agent: {Markup.Escape(task)}[/]");

        var context = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("yellow"))
            .StartAsync($"{agentType} agent working...", async _ =>
                await orchestrator.SpawnAgentAsync(task, agentType, ct));

        if (context.Status == AgentStatus.Completed)
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(agentType)} agent completed ({context.IterationCount} iterations)[/]");
            return context.Result ?? "(no result)";
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(agentType)} agent {context.Status}: {Markup.Escape(context.Error ?? "")}[/]");
            return $"Sub-agent failed: {context.Error}";
        }
    };
}

// Wire AskUserTool to Spectre.Console interactive prompt
var askUserTool = toolRegistry.Resolve("ask_user") as AskUserTool;
if (askUserTool is not null)
{
    askUserTool.UserPrompter = (question, options, ct) =>
    {
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(question)}[/]");

        var choices = new List<string>(options) { "Other (type custom response)" };

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select an option:")
                .PageSize(12)
                .AddChoices(choices));

        if (selected == "Other (type custom response)")
        {
            selected = AnsiConsole.Prompt(
                new TextPrompt<string>("[blue]Your response:[/]"));
        }

        return Task.FromResult(selected);
    };
}

// Create initial conversation
var conversationManager = host.Services.GetRequiredService<ConversationManager>();
conversationManager.CreateNew();

// Wire up REPL with tools
var repl = host.Services.GetRequiredService<ReplLoop>();
repl.SetTools(aiTools, ExecuteToolAsync);

// Restore session for --continue / --resume
var sessionManager = host.Services.GetRequiredService<SessionManager>();
if (continueSession || resumeSessionId is not null)
{
    SessionData? sessionToRestore = null;

    if (resumeSessionId is not null)
    {
        sessionToRestore = await sessionManager.LoadAsync(resumeSessionId);
        if (sessionToRestore is null)
        {
            AnsiConsole.MarkupLine($"[red]Session not found: {Markup.Escape(resumeSessionId)}[/]");
            return;
        }
    }
    else // --continue: most recent session
    {
        var sessions = sessionManager.List();
        if (sessions.Count > 0)
        {
            sessionToRestore = await sessionManager.LoadAsync(sessions[0].Id);
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]No saved sessions to continue.[/]");
        }
    }

    if (sessionToRestore is not null)
    {
        var restored = sessionManager.SessionToConversation(sessionToRestore);
        conversationManager.SetActive(sessionToRestore.Id, restored);
        repl.SetSessionId(sessionToRestore.Id);
        AnsiConsole.MarkupLine($"[green]Resumed session: {Markup.Escape(sessionToRestore.Title)} ({sessionToRestore.Messages.Count} messages)[/]");
    }
}

using var cts = new CancellationTokenSource();
var lastCtrlC = DateTime.MinValue;

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;

    // First Ctrl+C: try to cancel generation
    if (repl.TryCancelGeneration())
    {
        lastCtrlC = DateTime.UtcNow;
        AnsiConsole.MarkupLine("\n[yellow]Generation cancelled. Press Ctrl+C again to exit.[/]");
        return;
    }

    // Second Ctrl+C within 2 seconds: exit app
    if ((DateTime.UtcNow - lastCtrlC).TotalSeconds < 2)
    {
        cts.Cancel();
        return;
    }

    // No active generation — first press starts the timer
    lastCtrlC = DateTime.UtcNow;
    AnsiConsole.MarkupLine("\n[yellow]Press Ctrl+C again to exit.[/]");
};

// Check for --prompt "..." mode (single prompt, then exit)
var promptArgIndex = Array.IndexOf(args, "--prompt");
if (promptArgIndex >= 0 && promptArgIndex + 1 < args.Length)
{
    var singlePrompt = args[promptArgIndex + 1];
    if (jsonOutputMode)
        await repl.RunSinglePromptJsonAsync(singlePrompt, cts.Token);
    else
        await repl.RunSinglePromptAsync(singlePrompt, cts.Token);
}
else
{
    await repl.RunAsync(cts.Token);
}
