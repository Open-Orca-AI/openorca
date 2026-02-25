using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenOrca.Cli.CustomCommands;
using OpenOrca.Cli.Logging;
using OpenOrca.Cli.Rendering;
using OpenOrca.Cli.Repl;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Client;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Hooks;
using OpenOrca.Core.Orchestration;
using OpenOrca.Core.Permissions;
using OpenOrca.Core.Session;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Agent;
using OpenOrca.Tools.Interactive;
using OpenOrca.Tools.Registry;
using OpenOrca.Tools.Shell;
using OpenOrca.Tools.Utilities;
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

// Parse --print / -p as alias for --prompt + --output json
var printArgIndex = Array.FindIndex(args, a => a is "--print" or "-p");
string? printPrompt = null;
if (printArgIndex >= 0 && printArgIndex + 1 < args.Length)
{
    printPrompt = args[printArgIndex + 1];
    jsonOutputMode = true;
}

// Parse --sandbox / --simple flag
var sandboxMode = Array.Exists(args, a => a is "--sandbox" or "--simple");
if (sandboxMode) config.SandboxMode = true;

// Parse --allow-dir <path> flag
var allowDirIndex = Array.IndexOf(args, "--allow-dir");
if (allowDirIndex >= 0 && allowDirIndex + 1 < args.Length)
    config.AllowedDirectory = Path.GetFullPath(args[allowDirIndex + 1]);

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

// Register checkpoint manager
builder.Services.AddSingleton<CheckpointManager>();

// Register custom commands and memory
builder.Services.AddSingleton<CustomCommandLoader>();
builder.Services.AddSingleton<MemoryManager>();

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

// Wire shell config into BashTool
if (toolRegistry.Resolve("bash") is BashTool bashTool)
    bashTool.IdleTimeoutSeconds = config.Shell.IdleTimeoutSeconds;

// Initialize MCP servers (if configured)
OpenOrca.Core.Mcp.McpManager? mcpManager = null;
if (config.McpServers.Count > 0)
{
    mcpManager = new OpenOrca.Core.Mcp.McpManager(programLogger);
    try
    {
        var mcpToolPairs = await mcpManager.InitializeAsync(config.McpServers, CancellationToken.None);
        foreach (var (client, toolDef) in mcpToolPairs)
            toolRegistry.Register(new OpenOrca.Tools.Mcp.McpProxyTool(client, toolDef));
        if (mcpToolPairs.Count > 0)
            programLogger.LogInformation("Registered {Count} MCP tools", mcpToolPairs.Count);
    }
    catch (Exception ex)
    {
        programLogger.LogWarning(ex, "Failed to initialize MCP servers");
    }
}

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
var checkpointManager = host.Services.GetRequiredService<CheckpointManager>();
var replState = host.Services.GetRequiredService<ReplState>();

// Create tool executor with permission checks
async Task<string> ExecuteToolAsync(string toolName, string argsJson, CancellationToken ct)
{
    programLogger.LogDebug("ExecuteToolAsync called: {Tool} args: {Args}",
        toolName, argsJson.Length > 200 ? argsJson[..200] + "..." : argsJson);

    var tool = toolRegistry.Resolve(toolName);
    if (tool is null)
    {
        programLogger.LogWarning("Unknown tool requested: {Tool}", toolName);
        var suggestion = toolRegistry.FindClosestMatch(toolName);
        var hint = suggestion is not null ? $" Did you mean '{suggestion}'?" : "";
        return $"Unknown tool: {toolName}.{hint} Use only the tools listed in your system prompt.";
    }

    programLogger.LogDebug("Tool resolved: {Tool} (risk: {Risk})", toolName, tool.RiskLevel);

    // Permission check (with glob pattern matching)
    var approved = await permissionManager.CheckPermissionAsync(toolName, tool.RiskLevel.ToString(), argsJson);
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

    // Resolve parameter aliases (e.g., file_path → path) before validation
    var schema = tool.ParameterSchema;
    if (schema.TryGetProperty("properties", out _))
    {
        var resolved = ParameterAliasResolver.ResolveAliases(argsJson, schema, programLogger);
        if (resolved != argsJson)
        {
            programLogger.LogInformation("Resolved parameter aliases for {Tool}: {From} → {To}", toolName, argsJson, resolved);
            argsJson = resolved;
            argsElement = JsonDocument.Parse(argsJson).RootElement;
        }

        var inferred = ParameterAliasResolver.InferMissingRequired(argsJson, schema, programLogger);
        if (inferred != argsJson)
        {
            programLogger.LogInformation("Inferred missing required arg for {Tool}: {From} → {To}", toolName, argsJson, inferred);
            argsJson = inferred;
            argsElement = JsonDocument.Parse(argsJson).RootElement;
        }
    }

    // Snapshot file before modification (checkpoint)
    if (toolName is "edit_file" or "write_file" or "delete_file" or "copy_file" or "move_file" or "multi_edit")
    {
        var sid = replState.CurrentSessionId ?? "default";
        try
        {
            if (toolName == "multi_edit")
            {
                if (argsElement.TryGetProperty("edits", out var editsArr) && editsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var edit in editsArr.EnumerateArray())
                    {
                        if (edit.TryGetProperty("path", out var ep))
                            await checkpointManager.SnapshotAsync(ep.GetString()!, sid);
                    }
                }
            }
            else if (argsElement.TryGetProperty("path", out var pathProp))
            {
                await checkpointManager.SnapshotAsync(pathProp.GetString()!, sid);
            }

            if (toolName == "move_file" && argsElement.TryGetProperty("source", out var srcProp))
                await checkpointManager.SnapshotAsync(srcProp.GetString()!, sid);
        }
        catch (Exception cpEx)
        {
            programLogger.LogWarning(cpEx, "Failed to create checkpoint for {Tool}", toolName);
        }
    }

    // Validate required arguments before execution
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

    // Directory restriction check
    if (config.AllowedDirectory is not null &&
        toolName is "edit_file" or "write_file" or "delete_file" or "copy_file" or "move_file" or "multi_edit" or "read_file" or "glob" or "grep")
    {
        string? pathToCheck = null;
        if (argsElement.TryGetProperty("path", out var pathEl))
            pathToCheck = pathEl.GetString();
        else if (argsElement.TryGetProperty("directory", out var dirEl))
            pathToCheck = dirEl.GetString();

        if (pathToCheck is not null)
        {
            var fullPath = Path.GetFullPath(pathToCheck);
            var allowedFull = Path.GetFullPath(config.AllowedDirectory);
            if (!fullPath.StartsWith(allowedFull, StringComparison.OrdinalIgnoreCase))
            {
                programLogger.LogWarning("Tool {Tool} blocked — path {Path} is outside allowed directory {Dir}", toolName, fullPath, allowedFull);
                return $"ERROR: Path '{pathToCheck}' is outside the allowed directory '{config.AllowedDirectory}'. File operations are restricted.";
            }
        }
    }

    var toolStopwatch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        var result = await tool.ExecuteAsync(argsElement, ct);
        toolStopwatch.Stop();
        programLogger.LogDebug("Tool {Tool} completed: isError={IsError} ({Len} chars)",
            toolName, result.IsError, result.Content.Length);

        // Post-tool hook
        _ = hookRunner.RunPostHookAsync(toolName, argsJson, result.Content, result.IsError, ct);

        // Track tool call for JSON output
        replState.ToolCallHistory.Add(new OpenOrca.Cli.Serialization.ToolCallRecord
        {
            Name = toolName,
            Arguments = argsJson,
            Result = result.Content.Length > 1000 ? result.Content[..1000] + "..." : result.Content,
            IsError = result.IsError,
            DurationMs = toolStopwatch.ElapsedMilliseconds
        });

        // Track files modified
        if (!result.IsError && toolName is "edit_file" or "write_file" or "delete_file" or "copy_file" or "move_file" or "multi_edit")
        {
            if (argsElement.TryGetProperty("path", out var p))
                replState.FilesModified.Add(p.GetString()!);
            if (toolName == "multi_edit" && argsElement.TryGetProperty("edits", out var edits) && edits.ValueKind == JsonValueKind.Array)
            {
                foreach (var edit in edits.EnumerateArray())
                    if (edit.TryGetProperty("path", out var ep))
                        replState.FilesModified.Add(ep.GetString()!);
            }
        }

        // Prefix errors so the LLM clearly sees the tool failed and can adapt
        if (result.IsError)
            return $"ERROR: {result.Content}";

        return result.Content;
    }
    catch (Exception ex)
    {
        toolStopwatch.Stop();
        programLogger.LogError(ex, "Tool {Tool} execution threw unhandled exception", toolName);
        _ = hookRunner.RunPostHookAsync(toolName, argsJson, ex.Message, true, ct);

        replState.ToolCallHistory.Add(new OpenOrca.Cli.Serialization.ToolCallRecord
        {
            Name = toolName,
            Arguments = argsJson,
            Result = ex.Message,
            IsError = true,
            DurationMs = toolStopwatch.ElapsedMilliseconds
        });

        return $"ERROR executing {toolName}: {ex.Message}";
    }
}

// Streaming tool executor — delegates to IStreamingOrcaTool.ExecuteStreamingAsync
async Task<string> ExecuteToolStreamingAsync(string toolName, string argsJson, Action<string> onOutput, CancellationToken ct)
{
    programLogger.LogDebug("ExecuteToolStreamingAsync called: {Tool}", toolName);

    var tool = toolRegistry.Resolve(toolName);
    if (tool is null)
    {
        var suggestion = toolRegistry.FindClosestMatch(toolName);
        var hint = suggestion is not null ? $" Did you mean '{suggestion}'?" : "";
        return $"Unknown tool: {toolName}.{hint} This tool is not available.";
    }

    // Permission check
    var approved = await permissionManager.CheckPermissionAsync(toolName, tool.RiskLevel.ToString(), argsJson);
    if (!approved)
        return "Permission denied by user.";

    // Pre-tool hook
    if (!await hookRunner.RunPreHookAsync(toolName, argsJson, ct))
        return "Tool blocked by hook.";

    JsonElement argsElement;
    try
    {
        argsElement = JsonDocument.Parse(argsJson).RootElement;
    }
    catch
    {
        argsElement = JsonDocument.Parse("{}").RootElement;
    }

    // Resolve parameter aliases
    var schema = tool.ParameterSchema;
    if (schema.TryGetProperty("properties", out _))
    {
        var resolved = ParameterAliasResolver.ResolveAliases(argsJson, schema, programLogger);
        if (resolved != argsJson)
        {
            argsJson = resolved;
            argsElement = JsonDocument.Parse(argsJson).RootElement;
        }

        var inferred = ParameterAliasResolver.InferMissingRequired(argsJson, schema, programLogger);
        if (inferred != argsJson)
        {
            programLogger.LogInformation("Inferred missing required arg for {Tool}: {From} → {To}", toolName, argsJson, inferred);
            argsJson = inferred;
            argsElement = JsonDocument.Parse(argsJson).RootElement;
        }
    }

    // Validate required arguments
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
            return $"ERROR: {toolName} was called with missing required arguments: {string.Join(", ", missing)}.";
    }

    var toolStopwatch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        ToolResult result;
        if (tool is IStreamingOrcaTool streaming)
            result = await streaming.ExecuteStreamingAsync(argsElement, onOutput, ct);
        else
            result = await tool.ExecuteAsync(argsElement, ct);

        toolStopwatch.Stop();

        _ = hookRunner.RunPostHookAsync(toolName, argsJson, result.Content, result.IsError, ct);

        replState.ToolCallHistory.Add(new OpenOrca.Cli.Serialization.ToolCallRecord
        {
            Name = toolName,
            Arguments = argsJson,
            Result = result.Content.Length > 1000 ? result.Content[..1000] + "..." : result.Content,
            IsError = result.IsError,
            DurationMs = toolStopwatch.ElapsedMilliseconds
        });

        if (result.IsError)
            return $"ERROR: {result.Content}";

        return result.Content;
    }
    catch (Exception ex)
    {
        toolStopwatch.Stop();
        _ = hookRunner.RunPostHookAsync(toolName, argsJson, ex.Message, true, ct);

        replState.ToolCallHistory.Add(new OpenOrca.Cli.Serialization.ToolCallRecord
        {
            Name = toolName,
            Arguments = argsJson,
            Result = ex.Message,
            IsError = true,
            DurationMs = toolStopwatch.ElapsedMilliseconds
        });

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
        var taskPreview = task.Length > 80 ? task[..77] + "..." : task;
        AnsiConsole.MarkupLine($"  [yellow]\u25cf spawn_agent[/] [dim]{Markup.Escape(agentType)}: {Markup.Escape(taskPreview)}[/]");

        var context = await orchestrator.SpawnAgentAsync(task, agentType, ct);

        if (context.Status == AgentStatus.Completed)
        {
            AnsiConsole.MarkupLine($"  [green]\u2713 {Markup.Escape(agentType)} agent[/] [dim]({context.IterationCount} iterations)[/]");
            return context.Result ?? "(no result)";
        }
        else
        {
            AnsiConsole.MarkupLine($"  [red]\u2717 {Markup.Escape(agentType)} agent[/] [dim]{Markup.Escape(context.Error ?? "")}[/]");
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
repl.SetTools(aiTools, ExecuteToolAsync, ExecuteToolStreamingAsync);

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

// Check for --prompt "..." or --print "..." mode (single prompt, then exit)
var promptArgIndex = Array.IndexOf(args, "--prompt");
var singlePrompt = promptArgIndex >= 0 && promptArgIndex + 1 < args.Length
    ? args[promptArgIndex + 1]
    : printPrompt;

try
{
    if (singlePrompt is not null)
    {
        if (jsonOutputMode)
            await repl.RunSinglePromptJsonAsync(singlePrompt, cts.Token);
        else
            await repl.RunSinglePromptAsync(singlePrompt, cts.Token);
    }
    else
    {
        await repl.RunAsync(cts.Token);
    }
}
finally
{
    // Dispose MCP servers on exit
    if (mcpManager is not null)
        await mcpManager.DisposeAsync();
}
