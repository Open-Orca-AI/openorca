using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Parses tool calls from LLM text output when native function calling isn't used.
/// Also detects when the model should be nudged to use tool_call tags.
/// </summary>
internal sealed class ToolCallParser
{
    private readonly ILogger _logger;

    public ToolCallParser(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parse tool calls from the LLM's text output when native function calling isn't used.
    /// Handles common local-model formats: &lt;tool_call&gt; tags, ```json blocks, and raw JSON.
    /// Also handles R1-style models that wrap reasoning in &lt;think&gt; tags.
    /// </summary>
    public List<FunctionCallContent> ParseToolCallsFromText(string text)
    {
        var results = new List<FunctionCallContent>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        // Prefer the text OUTSIDE <think> blocks (the "action" portion).
        // Only fall back to searching inside <think> if nothing found outside.
        var stripped = Regex.Replace(text, @"<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
        // Also handle unclosed <think> (model started thinking and never closed it)
        stripped = Regex.Replace(stripped, @"<think>.*$", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
        // Strip <assistant> role tags that some models (Mistral) emit — these pollute tool call content
        stripped = Regex.Replace(stripped, @"<assistant>.*?</assistant>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
        stripped = Regex.Replace(stripped, @"</?assistant>", "", RegexOptions.IgnoreCase).Trim();

        // Search stripped text first (outside <think>), fall back to full text
        var textVariants = new List<string>();
        if (!string.IsNullOrWhiteSpace(stripped))
            textVariants.Add(stripped);
        textVariants.Add(text); // Full text as fallback

        var candidates = new List<string>();

        foreach (var variant in textVariants)
        {
            // Pattern 1-3: Tagged tool calls
            var tagPatterns = new[]
            {
                @"<tool_call>\s*(.*?)\s*</tool_call>",
                @"<\|tool_call\|>\s*(.*?)\s*<\|/tool_call\|>",
                @"\[TOOL_CALL\]\s*(.*?)\s*\[/TOOL_CALL\]",
                @"<function_call>\s*(.*?)\s*</function_call>",
            };

            foreach (var pattern in tagPatterns)
            {
                foreach (Match match in Regex.Matches(variant, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    candidates.Add(match.Groups[1].Value.Trim());
                }
            }

            // Pattern 4: JSON in code fences
            foreach (Match match in Regex.Matches(variant,
                @"```(?:json)?\s*\n?\s*(\{.*?\})\s*\n?\s*```", RegexOptions.Singleline))
            {
                candidates.Add(match.Groups[1].Value.Trim());
            }

            // Pattern 4b: Unclosed <tool_call> tag (model hit token limit mid-generation)
            // Only try if no closed tags were found — extract text after last <tool_call> to end
            if (candidates.Count == 0)
            {
                var unclosedMatch = Regex.Match(variant,
                    @"<tool_call>\s*(.*?)\s*$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (unclosedMatch.Success)
                {
                    candidates.Add(unclosedMatch.Groups[1].Value.Trim());
                }
            }

            // If we found candidates in this variant (stripped text), don't search full text
            // — avoids duplicates when both stripped and full text contain the same matches
            if (candidates.Count > 0)
                break;
        }

        // Pattern 5: Bare JSON objects that look like tool calls (only if no candidates yet)
        if (candidates.Count == 0)
        {
            foreach (var variant in textVariants)
            {
                foreach (Match match in Regex.Matches(variant,
                    @"\{[^{}]*""name""\s*:\s*""[^""]+""[^{}]*""arguments""\s*:\s*\{[^}]*\}[^{}]*\}",
                    RegexOptions.Singleline))
                {
                    candidates.Add(match.Value.Trim());
                }

                // Also try: {"tool_call": {...}} wrapper
                foreach (Match match in Regex.Matches(variant,
                    @"\{\s*""tool_call""\s*:\s*(\{.*?\})\s*\}", RegexOptions.Singleline))
                {
                    candidates.Add(match.Groups[1].Value.Trim());
                }

                // If we found candidates in this variant, don't search the next one
                // (avoids duplicates from inside <think> blocks)
                if (candidates.Count > 0)
                    break;
            }
        }

        // Try to parse each candidate
        foreach (var candidate in candidates)
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var root = doc.RootElement;

                string? name = null;
                IDictionary<string, object?>? arguments = null;

                // Format: {"name": "tool_name", "arguments": {...}}
                if (root.TryGetProperty("name", out var nameEl))
                {
                    name = nameEl.GetString();

                    if (root.TryGetProperty("arguments", out var argsEl) &&
                        argsEl.ValueKind == JsonValueKind.Object)
                    {
                        arguments = ParseArguments(argsEl);
                    }
                    else if (root.TryGetProperty("parameters", out var paramsEl) &&
                             paramsEl.ValueKind == JsonValueKind.Object)
                    {
                        arguments = ParseArguments(paramsEl);
                    }
                }
                // Format: {"function": {"name": "...", "arguments": {...}}}
                else if (root.TryGetProperty("function", out var funcEl) &&
                         funcEl.ValueKind == JsonValueKind.Object)
                {
                    name = funcEl.TryGetProperty("name", out var fnName) ? fnName.GetString() : null;
                    if (funcEl.TryGetProperty("arguments", out var fnArgs) &&
                        fnArgs.ValueKind == JsonValueKind.Object)
                    {
                        arguments = ParseArguments(fnArgs);
                    }
                }

                if (!string.IsNullOrEmpty(name))
                {
                    var callId = $"parsed_{Guid.NewGuid():N}";
                    results.Add(new FunctionCallContent(callId, name, arguments));
                }
            }
            catch (JsonException)
            {
                // Not valid JSON, skip
            }
        }

        return results;
    }

    /// <summary>
    /// Detect if the model's response looks like it tried to take action (code blocks with tool-like
    /// JSON, file content, shell commands) but didn't use &lt;tool_call&gt; tags.
    /// </summary>
    public bool ShouldNudgeForToolCalls(string text)
    {
        // Don't nudge if the text already has tool_call tags (shouldn't happen, but guard)
        if (text.Contains("<tool_call>", StringComparison.OrdinalIgnoreCase))
            return false;

        // Strip <think> blocks to look at the "action" portion only
        var actionText = Regex.Replace(text, @"<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
        actionText = Regex.Replace(actionText, @"<think>.*$", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();

        if (string.IsNullOrWhiteSpace(actionText))
            return false;

        // Check for code blocks that contain tool-like JSON (name + arguments)
        var hasCodeBlockWithToolJson = Regex.IsMatch(actionText,
            @"```(?:json)?\s*\n?\s*\{[^}]*""name""\s*:", RegexOptions.Singleline);
        if (hasCodeBlockWithToolJson)
        {
            _logger.LogDebug("Nudge trigger: code block with tool-like JSON");
            return true;
        }

        // Check for code blocks with file content when write_file/edit_file tools exist
        // Pattern: model shows file content in a code block and says "create" or "write" nearby
        var hasCodeBlock = actionText.Contains("```");
        var hasActionWords = Regex.IsMatch(actionText,
            @"\b(creat|writ|save|generat|implement|add|make|updat|modif)\w*\b",
            RegexOptions.IgnoreCase);
        var hasFilePath = Regex.IsMatch(actionText,
            @"[\w./\\]+\.\w{1,5}\b"); // something.ext

        if (hasCodeBlock && hasActionWords && hasFilePath)
        {
            _logger.LogDebug("Nudge trigger: code block + action words + file path");
            return true;
        }

        return false;
    }

    internal static IDictionary<string, object?> ParseArguments(JsonElement argsEl)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in argsEl.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => prop.Value.GetRawText()
            };
        }
        return dict;
    }
}
