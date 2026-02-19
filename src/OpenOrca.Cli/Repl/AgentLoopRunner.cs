using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Cli.Rendering;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;
using Spectre.Console;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Runs the agent loop: streaming, native/text tool switching, retry logic,
/// auto-compaction, server error probing, nudge, and generation cancellation.
/// </summary>
internal sealed class AgentLoopRunner
{
    internal const int MaxIterations = 25;

    private readonly IChatClient _chatClient;
    private readonly OrcaConfig _config;
    private readonly StreamingRenderer _streamingRenderer;
    private readonly ToolCallParser _toolCallParser;
    private readonly ToolCallExecutor _toolCallExecutor;
    private readonly CommandHandler _commandHandler;
    private readonly ReplState _state;
    private readonly ILogger _logger;

    /// <summary>
    /// Generation cancellation — allows Ctrl+C to cancel current generation without exiting.
    /// </summary>
    private CancellationTokenSource? _generationCts;

    public AgentLoopRunner(
        IChatClient chatClient,
        OrcaConfig config,
        StreamingRenderer streamingRenderer,
        ToolCallParser toolCallParser,
        ToolCallExecutor toolCallExecutor,
        CommandHandler commandHandler,
        ReplState state,
        ILogger logger)
    {
        _chatClient = chatClient;
        _config = config;
        _streamingRenderer = streamingRenderer;
        _toolCallParser = toolCallParser;
        _toolCallExecutor = toolCallExecutor;
        _commandHandler = commandHandler;
        _state = state;
        _logger = logger;
    }

    /// <summary>
    /// Try to cancel the current generation. Returns true if a generation was cancelled.
    /// </summary>
    public bool TryCancelGeneration()
    {
        if (_generationCts is { IsCancellationRequested: false })
        {
            _generationCts.Cancel();
            return true;
        }
        return false;
    }

    public async Task RunAgentLoopAsync(Conversation conversation, CancellationToken ct)
    {
        var nudgeAttempts = 0;
        _toolCallExecutor.ClearRecentErrors();

        // Auto-compact check
        if (_config.Context.AutoCompactEnabled)
        {
            var estimatedTokens = conversation.EstimateTokenCount();
            var threshold = (int)(_config.Context.ContextWindowSize * _config.Context.AutoCompactThreshold);
            if (estimatedTokens > threshold)
            {
                _logger.LogInformation("Auto-compacting: {Tokens} tokens exceeds threshold {Threshold}", estimatedTokens, threshold);
                await _commandHandler.CompactConversationAsync(conversation, null, ct);
            }
        }

        // Create generation-scoped CTS linked to the app-level token
        _generationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var genToken = _generationCts.Token;

        // Track whether native tools work with streaming
        var useNativeTools = _config.LmStudio.NativeToolCalling && _toolCallExecutor.Tools is { Count: > 0 };

        try
        {
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            _logger.LogDebug("=== Agent loop iteration {Iteration}/{Max} ===", iteration + 1, MaxIterations);

            var messages = conversation.GetMessagesForApi();
            _logger.LogDebug("Conversation has {Count} messages, sending to LLM", messages.Count);

            // Log message roles for debugging
            foreach (var m in messages)
            {
                var contentTypes = string.Join(", ",
                    m.Contents.Select(c => c.GetType().Name));
                _logger.LogDebug("  [{Role}] contents: {Types} ({Len} chars)",
                    m.Role.Value, contentTypes,
                    string.Join("", m.Contents.OfType<TextContent>().Select(t => t.Text)).Length);
            }

            _logger.LogDebug("Native tool calling: {Enabled}, tool count: {Count}",
                useNativeTools, _toolCallExecutor.Tools?.Count ?? 0);

            var options = new ChatOptions
            {
                Temperature = _config.LmStudio.Temperature,
                MaxOutputTokens = _config.LmStudio.MaxTokens,
                Tools = useNativeTools ? _toolCallExecutor.GetToolsForMode() : []
            };

            if (_config.LmStudio.Model is not null)
                options.ModelId = _config.LmStudio.Model;

            _streamingRenderer.Clear();

            // Capture the real stdout so the ThinkingIndicator can write to it
            var realStdout = Console.Out;

            using var thinking = new ThinkingIndicator(realStdout);
            var firstToken = true;
            var thinkingVisible = _state.ShowThinking;
            var consoleRedirected = false;

            var realStderr = Console.Error;
            void RedirectConsole()
            {
                if (!consoleRedirected)
                {
                    Console.SetOut(TextWriter.Null);
                    Console.SetError(TextWriter.Null);
                    consoleRedirected = true;
                }
            }
            void RestoreConsole()
            {
                if (consoleRedirected)
                {
                    Console.SetOut(realStdout);
                    Console.SetError(realStderr);
                    consoleRedirected = false;
                }
            }

            if (!thinkingVisible)
                RedirectConsole();

            try
            {
                _logger.LogDebug("Starting streaming request to LLM...");

                var textParts = new List<string>();
                var allContents = new List<AIContent>();
                var tokenCount = 0;

                var updateCount = 0;
                await foreach (var update in _chatClient.GetStreamingResponseAsync(
                    messages, options, genToken))
                {
                    updateCount++;

                    // Check for Ctrl+O toggle during streaming
                    while (!Console.IsInputRedirected && Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.O)
                        {
                            _state.ShowThinking = !_state.ShowThinking;
                            thinkingVisible = _state.ShowThinking;

                            if (!firstToken)
                            {
                                if (thinkingVisible)
                                {
                                    thinking.Stop();
                                    RestoreConsole();
                                    Console.Write("\x1b[36m");
                                    var buffered = string.Join("", textParts);
                                    Console.Write(buffered);
                                }
                                else
                                {
                                    _streamingRenderer.Finish();
                                    Console.Write("\x1b[0m\r\x1b[K");
                                    RedirectConsole();
                                }
                            }
                            else
                            {
                                if (thinkingVisible)
                                    RestoreConsole();
                                else
                                    RedirectConsole();
                            }
                        }
                    }

                    foreach (var content in update.Contents)
                    {
                        if (firstToken)
                        {
                            _logger.LogDebug("First token received from LLM");
                            if (thinkingVisible)
                            {
                                thinking.Stop();
                                RestoreConsole();
                                Console.Write("\x1b[36m");
                            }
                            firstToken = false;
                        }

                        if (content is TextContent textContent)
                        {
                            tokenCount++;
                            textParts.Add(textContent.Text);
                            if (thinkingVisible)
                                _streamingRenderer.AppendToken(textContent.Text);
                            else
                                thinking.UpdateTokenCount(tokenCount);
                        }
                        else if (content is FunctionCallContent fcc)
                        {
                            _logger.LogDebug("Native FunctionCallContent received: {Name} (id: {Id})",
                                fcc.Name, fcc.CallId);
                        }
                        else
                        {
                            _logger.LogDebug("Other content type received: {Type}", content.GetType().Name);
                        }

                        allContents.Add(content);
                    }
                }

                // Always restore console before any post-stream output
                RestoreConsole();

                if (firstToken && updateCount > 0 && useNativeTools)
                {
                    // Streaming with native tools returned updates but no content items.
                    // Retry WITHOUT native tools so the model uses text-based tool calling.
                    _logger.LogDebug("Stream had {UpdateCount} updates but 0 contents with native tools — retrying streaming without native tools", updateCount);

                    var retryOptions = new ChatOptions
                    {
                        Temperature = options.Temperature,
                        MaxOutputTokens = options.MaxOutputTokens,
                        ModelId = options.ModelId,
                        Tools = []
                    };

                    firstToken = true;
                    updateCount = 0;

                    await foreach (var update in _chatClient.GetStreamingResponseAsync(
                        messages, retryOptions, genToken))
                    {
                        updateCount++;

                        while (!Console.IsInputRedirected && Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(intercept: true);
                            if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.O)
                            {
                                _state.ShowThinking = !_state.ShowThinking;
                                thinkingVisible = _state.ShowThinking;
                                if (!firstToken)
                                {
                                    if (thinkingVisible) { thinking.Stop(); RestoreConsole(); Console.Write("\x1b[36m"); Console.Write(string.Join("", textParts)); }
                                    else { _streamingRenderer.Finish(); Console.Write("\x1b[0m\r\x1b[K"); RedirectConsole(); }
                                }
                                else { if (thinkingVisible) RestoreConsole(); else RedirectConsole(); }
                            }
                        }

                        foreach (var content in update.Contents)
                        {
                            if (firstToken)
                            {
                                _logger.LogDebug("First token received from retry stream");
                                if (thinkingVisible) { thinking.Stop(); RestoreConsole(); Console.Write("\x1b[36m"); }
                                firstToken = false;
                            }

                            if (content is TextContent tc)
                            {
                                tokenCount++;
                                textParts.Add(tc.Text);
                                if (thinkingVisible) _streamingRenderer.AppendToken(tc.Text);
                                else thinking.UpdateTokenCount(tokenCount);
                            }
                            allContents.Add(content);
                        }
                    }

                    RestoreConsole();
                    _logger.LogDebug("Retry stream complete: {UpdateCount} updates, {ContentCount} contents", updateCount, allContents.Count);

                    useNativeTools = false;
                    options.Tools = [];
                }

                if (firstToken)
                {
                    _logger.LogWarning("No tokens received from LLM — empty response");
                    thinking.Stop();

                    var serverError = await ProbeForServerErrorAsync(messages, options, genToken);
                    if (serverError is not null)
                        AnsiConsole.MarkupLine($"[red]LLM server error: {Markup.Escape(serverError)}[/]");
                    else
                        AnsiConsole.MarkupLine("[yellow]LLM returned an empty response.[/]");

                    ShowLogHint();
                    break;
                }

                var fullText = string.Join("", textParts);
                _state.LastAssistantResponse = fullText;
                _state.TotalOutputTokens += tokenCount;
                _logger.LogDebug("Streaming complete: {TokenCount} tokens, {CharCount} chars, {ContentCount} content items",
                    tokenCount, fullText.Length, allContents.Count);

                if (thinkingVisible)
                {
                    _streamingRenderer.Finish();
                    Console.Write("\x1b[0m");
                }
                else
                {
                    thinking.Stop();
                    if (fullText.Length > 0)
                    {
                        var preview = fullText.ReplaceLineEndings(" ");
                        if (preview.Length > 80)
                            preview = preview[..77] + "...";
                        Console.Write("\x1b[2m\x1b[36m");
                        Console.Write($"  [{tokenCount} tokens] {preview}");
                        Console.WriteLine("\x1b[0m");
                    }
                }

                // Build a ChatMessage from accumulated content
                var assistantMessage = new ChatMessage(ChatRole.Assistant, "");
                if (!string.IsNullOrEmpty(fullText))
                    assistantMessage.Contents.Add(new TextContent(fullText));

                // Collect native function calls
                var nativeFunctionCalls = allContents.OfType<FunctionCallContent>().ToList();
                foreach (var fc in nativeFunctionCalls)
                    assistantMessage.Contents.Add(fc);

                _logger.LogDebug("Native function calls: {Count}", nativeFunctionCalls.Count);

                // Fallback: if no native function calls, try parsing tool calls from text
                List<FunctionCallContent>? parsedFunctionCalls = null;
                if (nativeFunctionCalls.Count == 0 && !string.IsNullOrEmpty(fullText))
                {
                    var hasThink = fullText.Contains("<think>", StringComparison.OrdinalIgnoreCase);
                    var hasThinkClose = fullText.Contains("</think>", StringComparison.OrdinalIgnoreCase);
                    if (hasThink)
                    {
                        _logger.LogDebug("Model used <think> tags (closed: {Closed})", hasThinkClose);
                        if (hasThinkClose)
                        {
                            var afterThink = Regex.Replace(fullText, @"<think>.*?</think>", "",
                                RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
                            _logger.LogDebug("Content after </think> ({Len} chars): {Preview}",
                                afterThink.Length,
                                afterThink.Length > 500 ? afterThink[..500] : afterThink);
                        }
                        else
                        {
                            _logger.LogWarning("Model opened <think> but never closed it — entire response is reasoning");
                        }
                    }

                    parsedFunctionCalls = _toolCallParser.ParseToolCallsFromText(fullText);
                    _logger.LogDebug("Parsed function calls from text: {Count}", parsedFunctionCalls.Count);

                    if (parsedFunctionCalls.Count > 0)
                    {
                        foreach (var pc in parsedFunctionCalls)
                        {
                            _logger.LogInformation("Text-parsed tool call: {Name} args: {Args}",
                                pc.Name,
                                pc.Arguments is not null
                                    ? JsonSerializer.Serialize(pc.Arguments)
                                    : "{}");
                        }
                    }
                    else
                    {
                        _logger.LogDebug("No tool calls found. Full text ({Len} chars), first 500: {Preview}",
                            fullText.Length, fullText.Length > 500 ? fullText[..500] : fullText);
                        if (fullText.Length > 300)
                        {
                            _logger.LogDebug("Last 300 chars: {Tail}", fullText[^300..]);
                        }
                    }
                }

                if (nativeFunctionCalls.Count > 0)
                {
                    if (useNativeTools && _toolCallExecutor.HasMissingRequiredArgs(nativeFunctionCalls))
                    {
                        _logger.LogWarning(
                            "Native tool call(s) have empty arguments for tools with required params — " +
                            "SDK streaming likely lost the args. Auto-downgrading to text-based calling.");
                        useNativeTools = false;
                        options.Tools = [];
                        continue;
                    }

                    _logger.LogInformation("Executing {Count} native tool call(s)", nativeFunctionCalls.Count);
                    conversation.AddMessage(assistantMessage);
                    await _toolCallExecutor.ExecuteToolCallsAsync(nativeFunctionCalls, conversation, genToken);
                }
                else if (parsedFunctionCalls is { Count: > 0 })
                {
                    _logger.LogInformation("Executing {Count} text-parsed tool call(s)", parsedFunctionCalls.Count);
                    conversation.AddMessage(assistantMessage);
                    await _toolCallExecutor.ExecuteTextToolCallsAsync(parsedFunctionCalls, conversation, genToken);
                }
                else
                {
                    conversation.AddMessage(assistantMessage);

                    var hasOpenToolCall = fullText.Contains("<tool_call>", StringComparison.OrdinalIgnoreCase);
                    var hasCloseToolCall = fullText.Contains("</tool_call>", StringComparison.OrdinalIgnoreCase);

                    if (hasOpenToolCall && !hasCloseToolCall && nudgeAttempts < 2)
                    {
                        nudgeAttempts++;
                        _logger.LogInformation("Truncated tool call detected (attempt {Attempt}) — asking model to continue", nudgeAttempts);
                        AnsiConsole.MarkupLine("[yellow]Tool call was truncated — asking model to continue...[/]");

                        conversation.AddUserMessage(PromptConstants.TruncatedToolCallMessage);
                    }
                    else if (nudgeAttempts < 1 && !string.IsNullOrEmpty(fullText) && _toolCallParser.ShouldNudgeForToolCalls(fullText))
                    {
                        nudgeAttempts++;
                        _logger.LogInformation("Nudging model (attempt {Attempt}) — response has code blocks but no tool calls", nudgeAttempts);
                        AnsiConsole.MarkupLine("[yellow]Nudging model to use tool calls...[/]");

                        conversation.AddUserMessage(PromptConstants.NudgeMessage);
                    }
                    else
                    {
                        _logger.LogDebug("No tool calls — ending agent loop (nudge attempts: {Nudge})", nudgeAttempts);
                        break;
                    }
                }

                // Safety net: retry loop detection
                var maxFailure = _toolCallExecutor.GetMaxFailure();

                if (maxFailure.Count >= 4)
                {
                    _logger.LogWarning("Force-breaking agent loop — a tool has failed {Count} times identically", maxFailure.Count);
                    AnsiConsole.MarkupLine($"[yellow]Tool stuck in retry loop ({maxFailure.Count} identical failures) — stopping.[/]");
                    break;
                }

                if (maxFailure.Count >= 3)
                {
                    _logger.LogWarning("Injecting user message to redirect model after {Count} identical tool failures", maxFailure.Count);
                    conversation.AddUserMessage(PromptConstants.RetryLoopRedirectMessage);
                }
            }
            catch (HttpRequestException ex)
            {
                thinking.Stop();
                RestoreConsole();
                _streamingRenderer.Finish();
                Console.Write("\x1b[0m");
                _logger.LogError(ex, "HTTP error communicating with LLM server");
                AnsiConsole.MarkupLine($"\n[red]LLM server error: {Markup.Escape(ex.Message)}[/]");
                if (ex.InnerException is not null)
                    AnsiConsole.MarkupLine($"[red]  Inner: {Markup.Escape(ex.InnerException.Message)}[/]");
                AnsiConsole.MarkupLine($"[grey]  Endpoint: {Markup.Escape(_config.LmStudio.BaseUrl)}[/]");
                ShowLogHint();
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                thinking.Stop();
                RestoreConsole();
                _streamingRenderer.Finish();
                Console.Write("\x1b[0m");

                _logger.LogError(ex, "Error in agent loop iteration {Iteration}", iteration + 1);
                AnsiConsole.MarkupLine($"\n[red]Error: {Markup.Escape(ex.Message)}[/]");
                AnsiConsole.MarkupLine($"[grey]  Type: {ex.GetType().FullName}[/]");
                if (ex.InnerException is not null)
                    AnsiConsole.MarkupLine($"[grey]  Inner: {Markup.Escape(ex.InnerException.Message)}[/]");
                ShowLogHint();

                // Try non-streaming fallback
                _logger.LogWarning("Attempting non-streaming fallback...");
                AnsiConsole.MarkupLine("[yellow]Retrying without streaming...[/]");

                try
                {
                    var response = await _chatClient.GetResponseAsync(
                        conversation.GetMessagesForApi(), options, genToken);

                    _logger.LogDebug("Non-streaming response: {MsgCount} messages", response.Messages.Count);

                    var anyToolCalls = false;

                    foreach (var msg in response.Messages)
                    {
                        conversation.AddMessage(msg);

                        var text = string.Join("", msg.Contents.OfType<TextContent>().Select(t => t.Text));
                        if (!string.IsNullOrEmpty(text))
                        {
                            _logger.LogDebug("Non-streaming text ({Len} chars): {Preview}",
                                text.Length, text.Length > 100 ? text[..100] : text);
                            AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(text)}[/]");
                        }

                        var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
                        _logger.LogDebug("Non-streaming function calls: {Count}", functionCalls.Count);

                        if (functionCalls.Count == 0 && !string.IsNullOrEmpty(text))
                            functionCalls = _toolCallParser.ParseToolCallsFromText(text);

                        if (functionCalls.Count > 0)
                        {
                            anyToolCalls = true;
                            await _toolCallExecutor.ExecuteToolCallsAsync(functionCalls, conversation, genToken);
                        }
                    }

                    if (!anyToolCalls)
                    {
                        _logger.LogDebug("Non-streaming: no tool calls, ending loop");
                        break;
                    }
                }
                catch (Exception fallbackEx) when (fallbackEx is not OperationCanceledException)
                {
                    _logger.LogError(fallbackEx, "Non-streaming fallback also failed");
                    AnsiConsole.MarkupLine($"\n[red]Fallback also failed: {Markup.Escape(fallbackEx.Message)}[/]");
                    AnsiConsole.MarkupLine($"[grey]  Type: {fallbackEx.GetType().FullName}[/]");
                    if (fallbackEx.InnerException is not null)
                        AnsiConsole.MarkupLine($"[grey]  Inner: {Markup.Escape(fallbackEx.InnerException.Message)}[/]");
                    ShowLogHint();
                    break;
                }
            }
        }
        } // end try wrapping the for loop
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("Generation cancelled by user");
            AnsiConsole.MarkupLine("\n[yellow]Generation cancelled.[/]");
        }
        finally
        {
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    /// <summary>
    /// Makes a raw HTTP request to the LLM server to surface error details that the
    /// OpenAI SDK strips from its exceptions.
    /// </summary>
    private async Task<string?> ProbeForServerErrorAsync(
        List<ChatMessage> messages, ChatOptions options, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Probing server with raw HTTP request for error details...");

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.LmStudio.ApiKey}");

            var msgArray = messages.Select(m => new
            {
                role = m.Role.Value,
                content = string.Join("", m.Contents.OfType<TextContent>().Select(t => t.Text))
            });

            var payload = new
            {
                model = options.ModelId ?? _config.LmStudio.Model ?? "default",
                messages = msgArray,
                max_tokens = options.MaxOutputTokens ?? 50,
                temperature = options.Temperature ?? 0.7f,
                stream = false
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(
                $"{_config.LmStudio.BaseUrl.TrimEnd('/')}/chat/completions", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Server error probe got {Status}: {Body}", response.StatusCode, body);

                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("error", out var errorProp))
                    {
                        return errorProp.ValueKind == JsonValueKind.String
                            ? errorProp.GetString()
                            : errorProp.TryGetProperty("message", out var msgProp)
                                ? msgProp.GetString()
                                : body;
                    }
                }
                catch { /* not JSON, use raw body */ }

                return body;
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Error probe failed");
            return null;
        }
    }

    internal static void ShowLogHint()
    {
        var logDir = Path.Combine(Core.Configuration.ConfigManager.GetConfigDirectory(), "logs");
        AnsiConsole.MarkupLine($"[grey]  Logs: {Markup.Escape(logDir)}[/]");
    }
}
