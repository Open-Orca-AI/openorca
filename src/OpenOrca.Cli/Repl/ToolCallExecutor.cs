using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Cli.Rendering;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Serialization;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Registry;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Executes tool calls (both native and text-parsed) with plan mode enforcement,
/// sandbox mode enforcement, retry detection, parallel execution, and conversation state management.
/// </summary>
internal sealed class ToolCallExecutor
{
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolCallRenderer _toolCallRenderer;
    private readonly ReplState _state;
    private readonly OrcaConfig _config;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, (string Error, int Count)> _recentToolErrors = new();

    public Func<string, string, CancellationToken, Task<string>>? ToolExecutor { get; set; }
    public IList<AITool>? Tools { get; set; }

    /// <summary>
    /// Set by AgentLoopRunner to the real stdout so the progress indicator can render
    /// even when Console.Out is redirected.
    /// </summary>
    public TextWriter? RealStdout { get; set; }

    public ToolCallExecutor(
        ToolRegistry toolRegistry,
        ToolCallRenderer toolCallRenderer,
        ReplState state,
        OrcaConfig config,
        ILogger logger)
    {
        _toolRegistry = toolRegistry;
        _toolCallRenderer = toolCallRenderer;
        _state = state;
        _config = config;
        _logger = logger;
    }

    public void ClearRecentErrors() => _recentToolErrors.Clear();

    /// <summary>
    /// Get the maximum failure count among recent identical tool errors.
    /// </summary>
    public (string Error, int Count) GetMaxFailure()
    {
        return _recentToolErrors.Values
            .OrderByDescending(v => v.Count)
            .FirstOrDefault();
    }

    /// <summary>
    /// Execute native tool calls in parallel and add results to conversation using FunctionResultContent.
    /// Uses a 3-phase pattern: validate & render, execute in parallel, render results & commit.
    /// </summary>
    public async Task ExecuteToolCallsAsync(
        List<FunctionCallContent> functionCalls,
        Conversation conversation,
        CancellationToken ct)
    {
        const int maxArgsLength = 1_000_000; // 1MB

        // Phase 1 — Validate & render tool calls (sequential)
        var callData = new (string ToolName, string Args, FunctionCallContent Call, string? PreResult, bool PreError)[functionCalls.Count];

        for (int i = 0; i < functionCalls.Count; i++)
        {
            var call = functionCalls[i];
            var toolName = call.Name;
            var args = call.Arguments is not null
                ? JsonSerializer.Serialize(call.Arguments, OrcaJsonContext.Default.IDictionaryStringObject)
                : "{}";

            _logger.LogDebug("Preparing native tool: {Name} (callId: {Id}) args: {Args}",
                toolName, call.CallId, args.Length > 200 ? args[..200] + "..." : args);

            string? preResult = null;
            bool preError = false;

            // Validate input size
            if (args.Length > maxArgsLength)
            {
                preResult = $"Error: Tool arguments exceed maximum size ({args.Length} chars, limit {maxArgsLength}).";
                preError = true;
                _toolCallRenderer.RenderToolResult(toolName, preResult, isError: true);
            }
            // Block write/execute tools in plan mode
            else if (_state.PlanMode && !IsToolAllowedInPlanMode(toolName))
            {
                var orcaTool = _toolRegistry.Resolve(toolName);
                var risk = orcaTool?.RiskLevel.ToString() ?? "Unknown";
                preResult = $"PLAN MODE: Tool '{toolName}' is blocked in plan mode (risk: {risk}). " +
                            "Describe what you would do with this tool in your plan text instead.";
                preError = true;
                _toolCallRenderer.RenderPlanToolBlocked(toolName, risk);
                _logger.LogInformation("Blocked tool {Name} in plan mode (risk: {Risk})", toolName, risk);
            }
            // Block non-ReadOnly tools in sandbox mode
            else if (_config.SandboxMode && !IsToolAllowedInSandbox(toolName))
            {
                var orcaTool = _toolRegistry.Resolve(toolName);
                var risk = orcaTool?.RiskLevel.ToString() ?? "Unknown";
                preResult = $"SANDBOX MODE: Tool '{toolName}' is blocked in sandbox mode (risk: {risk}). " +
                            "Only read-only tools are available.";
                preError = true;
                _toolCallRenderer.RenderToolResult(toolName, preResult, isError: true);
                _logger.LogInformation("Blocked tool {Name} in sandbox mode (risk: {Risk})", toolName, risk);
            }
            else
            {
                _toolCallRenderer.RenderToolCall(toolName, args);
            }

            // Strip _reason before execution — it's only for display
            args = StripReason(args);
            callData[i] = (toolName, args, call, preResult, preError);
        }

        // Phase 2 — Execute in parallel (only calls that weren't pre-resolved)
        var results = new (string Result, bool IsError, TimeSpan Elapsed)[functionCalls.Count];
        var semaphore = new SemaphoreSlim(CliConstants.MaxParallelToolCalls);

        // Start progress indicator for tools that need execution
        var toolsToExecute = callData.Where(d => d.PreResult is null).Select(d => d.ToolName).ToList();
        using var progress = RealStdout is not null && toolsToExecute.Count > 0
            ? new ToolProgressIndicator(RealStdout, toolsToExecute)
            : null;

        var tasks = new List<Task>();
        for (int i = 0; i < callData.Length; i++)
        {
            var (toolName, args, call, preResult, preError) = callData[i];
            if (preResult is not null)
            {
                results[i] = (preResult, preError, TimeSpan.Zero);
                continue;
            }

            int index = i; // capture for closure
            tasks.Add(ExecuteSingleToolAsync(index, toolName, args, results, semaphore, progress, ct));
        }

        await Task.WhenAll(tasks);
        progress?.Stop();

        // If user cancelled during execution, skip Phase 3 — don't commit partial results
        ct.ThrowIfCancellationRequested();

        // Phase 3 — Render results & commit to conversation (sequential, in original order)
        for (int i = 0; i < callData.Length; i++)
        {
            var (toolName, args, call, preResult, _) = callData[i];
            var (result, isError, elapsed) = results[i];

            // Pre-resolved calls already rendered in Phase 1, just commit to conversation
            if (preResult is not null)
            {
                var errMsg = new ChatMessage(ChatRole.Tool, "");
                errMsg.Contents.Add(new FunctionResultContent(call.CallId, result));
                conversation.AddMessage(errMsg);
                continue;
            }

            result = ApplyRetryDetection(toolName, args, result);
            _toolCallRenderer.RenderToolResult(toolName, result, isError, elapsed);

            var toolResultMessage = new ChatMessage(ChatRole.Tool, "");
            toolResultMessage.Contents.Add(new FunctionResultContent(call.CallId, result));
            conversation.AddMessage(toolResultMessage);
            _logger.LogDebug("Added tool result message to conversation (callId: {Id})", call.CallId);
        }
    }

    /// <summary>
    /// Execute tool calls that were parsed from the LLM's text output, in parallel.
    /// Results are injected as a user message (not formal tool protocol) so the
    /// conversation stays valid for local models that don't support function calling natively.
    /// </summary>
    public async Task ExecuteTextToolCallsAsync(
        List<FunctionCallContent> functionCalls,
        Conversation conversation,
        CancellationToken ct)
    {
        // Phase 1 — Validate & render tool calls (sequential)
        var callData = new (string ToolName, string Args, string? PreResult, bool PreError)[functionCalls.Count];

        for (int i = 0; i < functionCalls.Count; i++)
        {
            var call = functionCalls[i];
            var toolName = call.Name;
            var args = call.Arguments is not null
                ? JsonSerializer.Serialize(call.Arguments, OrcaJsonContext.Default.IDictionaryStringObject)
                : "{}";

            _logger.LogDebug("Preparing text-parsed tool: {Name} args: {Args}",
                toolName, args.Length > 200 ? args[..200] + "..." : args);

            string? preResult = null;
            bool preError = false;

            // Block write/execute tools in plan mode
            if (_state.PlanMode && !IsToolAllowedInPlanMode(toolName))
            {
                var orcaTool = _toolRegistry.Resolve(toolName);
                var risk = orcaTool?.RiskLevel.ToString() ?? "Unknown";
                preResult = $"PLAN MODE: Tool '{toolName}' is blocked in plan mode (risk: {risk}). " +
                            "Describe what you would do with this tool in your plan text instead.";
                preError = true;
                _toolCallRenderer.RenderPlanToolBlocked(toolName, risk);
                _logger.LogInformation("Blocked text-parsed tool {Name} in plan mode (risk: {Risk})", toolName, risk);
            }
            // Block non-ReadOnly tools in sandbox mode
            else if (_config.SandboxMode && !IsToolAllowedInSandbox(toolName))
            {
                var orcaTool = _toolRegistry.Resolve(toolName);
                var risk = orcaTool?.RiskLevel.ToString() ?? "Unknown";
                preResult = $"SANDBOX MODE: Tool '{toolName}' is blocked in sandbox mode (risk: {risk}). " +
                            "Only read-only tools are available.";
                preError = true;
                _logger.LogInformation("Blocked text-parsed tool {Name} in sandbox mode (risk: {Risk})", toolName, risk);
            }
            else
            {
                _toolCallRenderer.RenderToolCall(toolName, args);
            }

            // Strip _reason before execution — it's only for display
            args = StripReason(args);
            callData[i] = (toolName, args, preResult, preError);
        }

        // Phase 2 — Execute in parallel (only calls that weren't pre-resolved)
        var results = new (string Result, bool IsError, TimeSpan Elapsed)[functionCalls.Count];
        var semaphore = new SemaphoreSlim(CliConstants.MaxParallelToolCalls);

        // Start progress indicator for tools that need execution
        var toolsToExecute = callData.Where(d => d.PreResult is null).Select(d => d.ToolName).ToList();
        using var progress = RealStdout is not null && toolsToExecute.Count > 0
            ? new ToolProgressIndicator(RealStdout, toolsToExecute)
            : null;

        var tasks = new List<Task>();
        for (int i = 0; i < callData.Length; i++)
        {
            var (toolName, args, preResult, preError) = callData[i];
            if (preResult is not null)
            {
                results[i] = (preResult, preError, TimeSpan.Zero);
                continue;
            }

            int index = i;
            tasks.Add(ExecuteSingleToolAsync(index, toolName, args, results, semaphore, progress, ct));
        }

        await Task.WhenAll(tasks);
        progress?.Stop();

        // If user cancelled during execution, skip Phase 3 — don't commit partial results
        ct.ThrowIfCancellationRequested();

        // Phase 3 — Render results & commit to conversation (sequential, in original order)
        var resultParts = new List<string>();

        for (int i = 0; i < callData.Length; i++)
        {
            var (toolName, args, preResult, _) = callData[i];
            var (result, isError, elapsed) = results[i];

            // Pre-resolved calls (plan mode) already rendered in Phase 1
            if (preResult is not null)
            {
                resultParts.Add($"[Tool result for {toolName}]\n{result}");
                continue;
            }

            result = ApplyRetryDetection(toolName, args, result);
            _toolCallRenderer.RenderToolResult(toolName, result, isError, elapsed);
            resultParts.Add($"[Tool result for {toolName}]\n{result}");
        }

        // Add results as a user message so the LLM sees them on the next turn
        var combined = string.Join("\n\n", resultParts);
        var injectedMessage = $"Here are the results of the tool calls you made:\n\n{combined}\n\nContinue with the task based on these results. If you need to use more tools, use <tool_call> tags.";
        conversation.AddUserMessage(injectedMessage);
        _logger.LogDebug("Injected tool results as user message ({Len} chars)", injectedMessage.Length);
    }

    /// <summary>
    /// Execute a single tool with semaphore throttling and a global timeout safety net.
    /// Writes result to the shared results array.
    /// </summary>
    private async Task ExecuteSingleToolAsync(
        int index,
        string toolName,
        string args,
        (string Result, bool IsError, TimeSpan Elapsed)[] results,
        SemaphoreSlim semaphore,
        ToolProgressIndicator? progress,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        var sw = Stopwatch.StartNew();
        try
        {
            if (ToolExecutor is not null)
            {
                // Create a linked CTS: fires on user cancellation OR global tool timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(CliConstants.ToolExecutionTimeoutSeconds));

                try
                {
                    var result = await ToolExecutor(toolName, args, timeoutCts.Token);
                    sw.Stop();
                    _logger.LogDebug("Tool {Name} returned {Len} chars in {Elapsed:F1}s", toolName, result.Length, sw.Elapsed.TotalSeconds);
                    results[index] = (result, false, sw.Elapsed);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    sw.Stop();
                    // Timeout fired but user didn't cancel — this is a tool timeout
                    _logger.LogWarning("Tool {Name} timed out after {Timeout}s",
                        toolName, CliConstants.ToolExecutionTimeoutSeconds);
                    results[index] = ($"Error: Tool '{toolName}' timed out after {CliConstants.ToolExecutionTimeoutSeconds} seconds. " +
                        "If this command runs indefinitely (server, watcher, REPL), use start_background_process instead.", true, sw.Elapsed);
                }
                catch (OperationCanceledException)
                {
                    // User cancelled — propagate
                    throw;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    results[index] = ($"Error: {ex.Message}", true, sw.Elapsed);
                    _logger.LogWarning(ex, "Tool {Name} threw exception", toolName);
                }
            }
            else
            {
                sw.Stop();
                results[index] = ("Tool execution not available.", true, sw.Elapsed);
                _logger.LogWarning("Tool executor not set — cannot execute {Name}", toolName);
            }
        }
        finally
        {
            progress?.MarkCompleted(toolName);
            semaphore.Release();
        }
    }

    /// <summary>
    /// Check if a tool is allowed to execute in plan mode.
    /// Only ReadOnly tools can run; Moderate and Dangerous tools are blocked.
    /// </summary>
    public bool IsToolAllowedInPlanMode(string toolName)
    {
        var tool = _toolRegistry.Resolve(toolName);
        if (tool is null) return false; // Unknown tools are blocked
        return tool.RiskLevel == ToolRiskLevel.ReadOnly;
    }

    /// <summary>
    /// Check if a tool is allowed in sandbox mode.
    /// Only ReadOnly tools can run.
    /// </summary>
    public bool IsToolAllowedInSandbox(string toolName)
    {
        var tool = _toolRegistry.Resolve(toolName);
        if (tool is null) return false;
        return tool.RiskLevel == ToolRiskLevel.ReadOnly;
    }

    /// <summary>
    /// Get the tools list filtered for the current mode.
    /// In plan mode or sandbox mode, only read-only tools are included.
    /// </summary>
    public IList<AITool> GetToolsForMode()
    {
        if (Tools is null) return [];

        var restrictToReadOnly = _state.PlanMode || _config.SandboxMode;
        if (!restrictToReadOnly) return Tools;

        return Tools.Where(t =>
        {
            if (t is not AIFunction af) return true;
            return IsToolAllowedInPlanMode(af.Name);
        }).ToList();
    }

    /// <summary>
    /// Check if any native function calls have empty arguments for tools with required parameters.
    /// This indicates the SDK streaming lost the argument payload (common with large args like file content).
    /// </summary>
    public bool HasMissingRequiredArgs(List<FunctionCallContent> functionCalls)
    {
        if (Tools is null) return false;

        foreach (var fc in functionCalls)
        {
            if (fc.Arguments is not null && fc.Arguments.Count > 0)
                continue; // This call has args, it's fine

            // Look up the tool definition to check for required parameters
            var toolDef = Tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == fc.Name);
            if (toolDef is null) continue;

            try
            {
                var schema = toolDef.JsonSchema;
                if (schema.TryGetProperty("required", out var reqArr) &&
                    reqArr.ValueKind == JsonValueKind.Array &&
                    reqArr.GetArrayLength() > 0)
                {
                    _logger.LogDebug("Native tool call {Name} has empty args but tool requires: {Required}",
                        fc.Name, reqArr.ToString());
                    return true;
                }
            }
            catch (Exception) { /* schema inspection failed, skip */ }
        }

        return false;
    }

    /// <summary>
    /// Remove the _reason field from tool arguments JSON so it doesn't reach the tool executor.
    /// </summary>
    private static string StripReason(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson) || argsJson == "{}" || !argsJson.Contains("_reason"))
            return argsJson;

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            if (!doc.RootElement.TryGetProperty("_reason", out _))
                return argsJson;

            using var ms = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name != "_reason")
                        prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return argsJson;
        }
    }

    private string ApplyRetryDetection(string toolName, string argsJson, string result)
    {
        var key = $"{toolName}::{argsJson}";

        if (!result.StartsWith("ERROR:", StringComparison.Ordinal))
        {
            _recentToolErrors.TryRemove(key, out _);
            return result;
        }

        var newEntry = _recentToolErrors.AddOrUpdate(
            key,
            _ => (result, 1),
            (_, prev) => prev.Error == result ? (result, prev.Count + 1) : (result, 1));

        if (newEntry.Count >= 3)
        {
            _logger.LogWarning("Tool {Name} has failed {Count} times with same error — forcing stop", toolName, newEntry.Count);
            return $"STOP: This tool call has failed {newEntry.Count} times with the same error. You MUST use a different approach. Original error: {result}";
        }

        if (newEntry.Count >= 2)
        {
            _logger.LogWarning("Tool {Name} has failed {Count} times with same error — warning appended", toolName, newEntry.Count);
            return result + "\n\nWARNING: This tool call has failed 2 times with the same error. Do NOT retry with the same arguments. Try a different approach or tool.";
        }

        return result;
    }
}
