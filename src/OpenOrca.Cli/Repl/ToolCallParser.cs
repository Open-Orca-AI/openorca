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
    // Tag stripping patterns
    private static readonly Regex ThinkBlockRegex = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ThinkUnclosedRegex = new(@"<think>.*$", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AssistantBlockRegex = new(@"<assistant>.*?</assistant>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AssistantTagRegex = new(@"</?assistant>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Tool call tag patterns (1-4)
    private static readonly Regex[] TagPatterns =
    [
        new(@"<tool_call>\s*(.*?)\s*</tool_call>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<\|tool_call\|>\s*(.*?)\s*<\|/tool_call\|>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\[TOOL_CALL\]\s*(.*?)\s*\[/TOOL_CALL\]", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<function_call>\s*(.*?)\s*</function_call>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    // JSON in code fences
    private static readonly Regex CodeFenceJsonRegex = new(@"```(?:json)?\s*\n?\s*(\{.*?\})\s*\n?\s*```", RegexOptions.Singleline | RegexOptions.Compiled);

    // Unclosed tool_call tag
    private static readonly Regex UnclosedToolCallRegex = new(@"<tool_call>\s*(.*?)\s*$", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Bare JSON tool call patterns (pattern 5)
    // Supports one level of nested braces in arguments (e.g. {"opts": {"key": "val"}})
    private static readonly Regex BareJsonToolCallRegex = new(
        @"\{\s*""name""\s*:\s*""[^""]+""[^{}]*""arguments""\s*:\s*\{(?:[^{}]|\{[^{}]*\})*\}\s*\}",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex WrappedToolCallRegex = new(@"\{\s*""tool_call""\s*:\s*(\{.*?\})\s*\}", RegexOptions.Singleline | RegexOptions.Compiled);

    // Nudge detection patterns
    private static readonly Regex CodeBlockToolJsonRegex = new(@"```(?:json)?\s*\n?\s*\{[^}]*""name""\s*:", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ActionWordsRegex = new(@"\b(creat|writ|save|generat|implement|add|make|updat|modif)\w*\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FilePathRegex = new(@"[\w./\\]+\.\w{1,5}\b", RegexOptions.Compiled);

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
        var stripped = ThinkBlockRegex.Replace(text, "").Trim();
        // Also handle unclosed <think> (model started thinking and never closed it)
        stripped = ThinkUnclosedRegex.Replace(stripped, "").Trim();
        // Strip <assistant> role tags that some models (Mistral) emit — these pollute tool call content
        stripped = AssistantBlockRegex.Replace(stripped, "").Trim();
        stripped = AssistantTagRegex.Replace(stripped, "").Trim();

        // Search stripped text first (outside <think>), fall back to full text
        var textVariants = new List<string>();
        if (!string.IsNullOrWhiteSpace(stripped))
            textVariants.Add(stripped);
        textVariants.Add(text); // Full text as fallback

        var candidates = new List<string>();

        foreach (var variant in textVariants)
        {
            // Pattern 1-3: Tagged tool calls
            foreach (var pattern in TagPatterns)
            {
                foreach (Match match in pattern.Matches(variant))
                {
                    candidates.Add(match.Groups[1].Value.Trim());
                }
            }

            // Pattern 4: JSON in code fences
            foreach (Match match in CodeFenceJsonRegex.Matches(variant))
            {
                candidates.Add(match.Groups[1].Value.Trim());
            }

            // Pattern 4b: Unclosed <tool_call> tag (model hit token limit mid-generation)
            // Only try if no closed tags were found — extract text after last <tool_call> to end
            if (candidates.Count == 0)
            {
                var unclosedMatch = UnclosedToolCallRegex.Match(variant);
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
                foreach (Match match in BareJsonToolCallRegex.Matches(variant))
                {
                    candidates.Add(match.Value.Trim());
                }

                // Also try: {"tool_call": {...}} wrapper
                foreach (Match match in WrappedToolCallRegex.Matches(variant))
                {
                    candidates.Add(match.Groups[1].Value.Trim());
                }

                // If we found candidates in this variant, don't search the next one
                // (avoids duplicates from inside <think> blocks)
                if (candidates.Count > 0)
                    break;
            }
        }

        // Try to parse each candidate — a candidate may contain multiple JSON objects
        // (some models emit several tool calls in a single <tool_call> block)
        foreach (var candidate in candidates)
        {
            foreach (var jsonStr in SplitJsonObjects(candidate))
            {
                TryParseToolCall(jsonStr, results);
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
        var actionText = ThinkBlockRegex.Replace(text, "").Trim();
        actionText = ThinkUnclosedRegex.Replace(actionText, "").Trim();

        if (string.IsNullOrWhiteSpace(actionText))
            return false;

        // Check for code blocks that contain tool-like JSON (name + arguments)
        if (CodeBlockToolJsonRegex.IsMatch(actionText))
        {
            _logger.LogDebug("Nudge trigger: code block with tool-like JSON");
            return true;
        }

        // Check for code blocks with file content when write_file/edit_file tools exist
        // Pattern: model shows file content in a code block and says "create" or "write" nearby
        var hasCodeBlock = actionText.Contains("```");
        var hasActionWords = ActionWordsRegex.IsMatch(actionText);
        var hasFilePath = FilePathRegex.IsMatch(actionText); // something.ext

        if (hasCodeBlock && hasActionWords && hasFilePath)
        {
            _logger.LogDebug("Nudge trigger: code block + action words + file path");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Try to parse a single JSON string as a tool call and add it to results.
    /// </summary>
    private static void TryParseToolCall(string json, List<FunctionCallContent> results)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
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

    /// <summary>
    /// Split a string that may contain multiple concatenated JSON objects into individual JSON strings.
    /// Handles: "{...}\n{...}\n{...}" — common when models emit multiple tool calls in one block.
    /// Uses brace-depth counting to find object boundaries.
    /// </summary>
    internal static List<string> SplitJsonObjects(string text)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return results;

        // Fast path: if it parses as a single valid JSON object, return it directly
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                results.Add(text);
                return results;
            }
        }
        catch (JsonException)
        {
            // Not a single JSON object — fall through to multi-object splitting
        }

        // Brace-depth scanning: find each top-level { ... } object
        var depth = 0;
        var inString = false;
        var escape = false;
        var start = -1;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
                continue;

            if (ch == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    results.Add(text[start..(i + 1)]);
                    start = -1;
                }
            }
        }

        return results;
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
