using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Cli.Rendering;
using OpenOrca.Core.Chat;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Registry;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Executes tool calls (both native and text-parsed) with plan mode enforcement,
/// retry detection, and conversation state management.
/// </summary>
internal sealed class ToolCallExecutor
{
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolCallRenderer _toolCallRenderer;
    private readonly ReplState _state;
    private readonly ILogger _logger;
    private readonly Dictionary<string, (string Error, int Count)> _recentToolErrors = new();

    public Func<string, string, CancellationToken, Task<string>>? ToolExecutor { get; set; }
    public IList<AITool>? Tools { get; set; }

    public ToolCallExecutor(
        ToolRegistry toolRegistry,
        ToolCallRenderer toolCallRenderer,
        ReplState state,
        ILogger logger)
    {
        _toolRegistry = toolRegistry;
        _toolCallRenderer = toolCallRenderer;
        _state = state;
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
    /// Execute native tool calls and add results to conversation using FunctionResultContent.
    /// </summary>
    public async Task ExecuteToolCallsAsync(
        List<FunctionCallContent> functionCalls,
        Conversation conversation,
        CancellationToken ct)
    {
        foreach (var call in functionCalls)
        {
            var toolName = call.Name;
            var args = call.Arguments is not null
                ? JsonSerializer.Serialize(call.Arguments)
                : "{}";

            _logger.LogDebug("Executing native tool: {Name} (callId: {Id}) args: {Args}",
                toolName, call.CallId, args.Length > 200 ? args[..200] + "..." : args);

            _toolCallRenderer.RenderToolCall(toolName, args);

            string result;
            bool isError = false;

            // Block write/execute tools in plan mode
            if (_state.PlanMode && !IsToolAllowedInPlanMode(toolName))
            {
                var orcaTool = _toolRegistry.Resolve(toolName);
                var risk = orcaTool?.RiskLevel.ToString() ?? "Unknown";
                result = $"PLAN MODE: Tool '{toolName}' is blocked in plan mode (risk: {risk}). " +
                         "Describe what you would do with this tool in your plan text instead.";
                isError = true;
                _toolCallRenderer.RenderPlanToolBlocked(toolName, risk);
                _logger.LogInformation("Blocked tool {Name} in plan mode (risk: {Risk})", toolName, risk);
            }
            else if (ToolExecutor is not null)
            {
                try
                {
                    result = await ToolExecutor(toolName, args, ct);
                    _logger.LogDebug("Tool {Name} returned {Len} chars", toolName, result.Length);
                }
                catch (Exception ex)
                {
                    result = $"Error: {ex.Message}";
                    isError = true;
                    _logger.LogWarning(ex, "Tool {Name} threw exception", toolName);
                }
            }
            else
            {
                result = "Tool execution not available.";
                isError = true;
                _logger.LogWarning("Tool executor not set — cannot execute {Name}", toolName);
            }

            result = ApplyRetryDetection(toolName, args, result);
            _toolCallRenderer.RenderToolResult(toolName, result, isError);

            var toolResultMessage = new ChatMessage(ChatRole.Tool, "");
            toolResultMessage.Contents.Add(new FunctionResultContent(
                call.CallId, result));
            conversation.AddMessage(toolResultMessage);
            _logger.LogDebug("Added tool result message to conversation (callId: {Id})", call.CallId);
        }
    }

    /// <summary>
    /// Execute tool calls that were parsed from the LLM's text output.
    /// Results are injected as a user message (not formal tool protocol) so the
    /// conversation stays valid for local models that don't support function calling natively.
    /// </summary>
    public async Task ExecuteTextToolCallsAsync(
        List<FunctionCallContent> functionCalls,
        Conversation conversation,
        CancellationToken ct)
    {
        var resultParts = new List<string>();

        foreach (var call in functionCalls)
        {
            var toolName = call.Name;
            var args = call.Arguments is not null
                ? JsonSerializer.Serialize(call.Arguments)
                : "{}";

            _logger.LogDebug("Executing text-parsed tool: {Name} args: {Args}",
                toolName, args.Length > 200 ? args[..200] + "..." : args);

            _toolCallRenderer.RenderToolCall(toolName, args);

            string result;
            bool isError = false;

            // Block write/execute tools in plan mode
            if (_state.PlanMode && !IsToolAllowedInPlanMode(toolName))
            {
                var orcaTool = _toolRegistry.Resolve(toolName);
                var risk = orcaTool?.RiskLevel.ToString() ?? "Unknown";
                result = $"PLAN MODE: Tool '{toolName}' is blocked in plan mode (risk: {risk}). " +
                         "Describe what you would do with this tool in your plan text instead.";
                isError = true;
                _toolCallRenderer.RenderPlanToolBlocked(toolName, risk);
                _logger.LogInformation("Blocked text-parsed tool {Name} in plan mode (risk: {Risk})", toolName, risk);
            }
            else if (ToolExecutor is not null)
            {
                try
                {
                    result = await ToolExecutor(toolName, args, ct);
                    _logger.LogDebug("Text-parsed tool {Name} returned {Len} chars", toolName, result.Length);
                }
                catch (Exception ex)
                {
                    result = $"Error: {ex.Message}";
                    isError = true;
                    _logger.LogWarning(ex, "Text-parsed tool {Name} threw exception", toolName);
                }
            }
            else
            {
                result = "Tool execution not available.";
                isError = true;
                _logger.LogWarning("Tool executor not set — cannot execute {Name}", toolName);
            }

            result = ApplyRetryDetection(toolName, args, result);
            _toolCallRenderer.RenderToolResult(toolName, result, isError);

            resultParts.Add($"[Tool result for {toolName}]\n{result}");
        }

        // Add results as a user message so the LLM sees them on the next turn
        var combined = string.Join("\n\n", resultParts);
        var injectedMessage = $"Here are the results of the tool calls you made:\n\n{combined}\n\nContinue with the task based on these results. If you need to use more tools, use <tool_call> tags.";
        conversation.AddUserMessage(injectedMessage);
        _logger.LogDebug("Injected tool results as user message ({Len} chars)", injectedMessage.Length);
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
    /// Get the tools list filtered for the current mode.
    /// In plan mode, only read-only tools are included.
    /// </summary>
    public IList<AITool> GetToolsForMode()
    {
        if (Tools is null) return [];
        if (!_state.PlanMode) return Tools;

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
            catch { /* schema inspection failed, skip */ }
        }

        return false;
    }

    private string ApplyRetryDetection(string toolName, string argsJson, string result)
    {
        var key = $"{toolName}::{argsJson}";

        if (!result.StartsWith("ERROR:", StringComparison.Ordinal))
        {
            _recentToolErrors.Remove(key);
            return result;
        }

        if (_recentToolErrors.TryGetValue(key, out var prev) && prev.Error == result)
        {
            var count = prev.Count + 1;
            _recentToolErrors[key] = (result, count);

            if (count >= 3)
            {
                _logger.LogWarning("Tool {Name} has failed {Count} times with same error — forcing stop", toolName, count);
                return $"STOP: This tool call has failed {count} times with the same error. You MUST use a different approach. Original error: {result}";
            }

            _logger.LogWarning("Tool {Name} has failed {Count} times with same error — warning appended", toolName, count);
            return result + "\n\nWARNING: This tool call has failed 2 times with the same error. Do NOT retry with the same arguments. Try a different approach or tool.";
        }

        _recentToolErrors[key] = (result, 1);
        return result;
    }
}
