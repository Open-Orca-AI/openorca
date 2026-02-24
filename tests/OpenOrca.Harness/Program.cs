using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenOrca.Core.Client;
using OpenOrca.Core.Configuration;
using OpenOrca.Tools.Registry;

// ── OpenOrca Diagnostic Harness ──
// Runs a series of tests against the configured LM Studio endpoint
// to diagnose connectivity, model behaviour, and tool calling support.

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║   OpenOrca Diagnostic Harness            ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();

// Load config
var configManager = new ConfigManager();
await configManager.LoadAsync();
var config = configManager.Config;

Console.WriteLine($"Endpoint : {config.LmStudio.BaseUrl}");
Console.WriteLine($"API Key  : {config.LmStudio.ApiKey[..3]}***");
Console.WriteLine($"Model    : {config.LmStudio.Model ?? "(auto/default)"}");
Console.WriteLine($"Native Tool Calling : {config.LmStudio.NativeToolCalling}");
Console.WriteLine();

var passed = 0;
var failed = 0;
var skipped = 0;

// ═══════════════════════════════════════════
// Test 1: Connectivity
// ═══════════════════════════════════════════
await RunTestAsync("1. LM Studio Connectivity", async () =>
{
    var discovery = new ModelDiscovery(config, NullLogger<ModelDiscovery>.Instance);
    var models = await discovery.GetAvailableModelsAsync();

    if (models.Count == 0)
        throw new Exception("No models found — is LM Studio running?");

    Console.WriteLine($"   Found {models.Count} model(s):");
    foreach (var m in models)
        Console.WriteLine($"     - {m}");

    return true;
});

// ═══════════════════════════════════════════
// Test 2: Simple Chat (non-streaming, no tools)
// ═══════════════════════════════════════════
await RunTestAsync("2. Simple Chat (non-streaming, no tools)", async () =>
{
    var client = CreateClient(config);

    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, "You are a helpful assistant. Reply concisely."),
        new(ChatRole.User, "What is 2 + 2? Reply with just the number.")
    };

    var options = new ChatOptions
    {
        Temperature = 0.1f,
        MaxOutputTokens = 50
    };

    if (config.LmStudio.Model is not null)
        options.ModelId = config.LmStudio.Model;

    var response = await client.GetResponseAsync(messages, options);

    var text = string.Join("", response.Messages
        .SelectMany(m => m.Contents.OfType<TextContent>())
        .Select(t => t.Text));

    if (string.IsNullOrWhiteSpace(text))
        throw new Exception("Empty response from non-streaming chat");

    Console.WriteLine($"   Response: {Truncate(text, 200)}");
    return true;
});

// ═══════════════════════════════════════════
// Test 3: Streaming Chat (no tools)
// ═══════════════════════════════════════════
await RunTestAsync("3. Streaming Chat (no tools)", async () =>
{
    var client = CreateClient(config);

    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, "You are a helpful assistant. Reply concisely."),
        new(ChatRole.User, "Say hello in exactly 5 words.")
    };

    var options = new ChatOptions
    {
        Temperature = 0.1f,
        MaxOutputTokens = 50
    };

    if (config.LmStudio.Model is not null)
        options.ModelId = config.LmStudio.Model;

    var tokenCount = 0;
    var parts = new List<string>();

    await foreach (var update in client.GetStreamingResponseAsync(messages, options))
    {
        foreach (var content in update.Contents)
        {
            if (content is TextContent tc)
            {
                tokenCount++;
                parts.Add(tc.Text);
            }
        }
    }

    var fullText = string.Join("", parts);
    if (string.IsNullOrWhiteSpace(fullText))
    {
        // Streaming errors come as SSE error events (HTTP 200) that the SDK drops silently.
        // Make a raw HTTP probe to get the actual error body.
        Console.WriteLine("   No tokens from streaming — probing server for error details...");
        var serverError = await ProbeServerErrorAsync(config,
            [new { role = "user", content = "Say hello in exactly 5 words." }]);
        if (serverError is not null)
            throw new Exception($"LLM server error (streaming masked it): {serverError}");
        throw new Exception("No tokens received from streaming (server returned OK — genuinely empty)");
    }

    Console.WriteLine($"   Tokens: {tokenCount}, Text: {Truncate(fullText, 200)}");
    return true;
});

// ═══════════════════════════════════════════
// Test 4: Streaming with Native Tool Definitions
// ═══════════════════════════════════════════
await RunTestAsync("4. Streaming with Native Tool Definitions", async () =>
{
    var client = CreateClient(config);

    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, "You are a helpful assistant with tools."),
        new(ChatRole.User, "What is 2 + 2? Reply with just the number.")
    };

    // Define a simple dummy tool
    var toolSchema = JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "expression": {
                "type": "string",
                "description": "The math expression to evaluate"
            }
        },
        "required": ["expression"]
    }
    """).RootElement;

    var tools = new List<AITool>
    {
        AIFunctionFactory.Create((string expression) => $"Result: {expression}", "calculator",
            "Evaluates a math expression")
    };

    var options = new ChatOptions
    {
        Temperature = 0.1f,
        MaxOutputTokens = 100,
        Tools = tools
    };

    if (config.LmStudio.Model is not null)
        options.ModelId = config.LmStudio.Model;

    var tokenCount = 0;
    var parts = new List<string>();
    var functionCalls = new List<string>();

    await foreach (var update in client.GetStreamingResponseAsync(messages, options))
    {
        foreach (var content in update.Contents)
        {
            if (content is TextContent tc)
            {
                tokenCount++;
                parts.Add(tc.Text);
            }
            else if (content is FunctionCallContent fcc)
            {
                functionCalls.Add(fcc.Name);
            }
        }
    }

    var fullText = string.Join("", parts);
    Console.WriteLine($"   Tokens: {tokenCount}, Text length: {fullText.Length}");
    Console.WriteLine($"   Function calls: {(functionCalls.Count > 0 ? string.Join(", ", functionCalls) : "(none)")}");

    if (string.IsNullOrWhiteSpace(fullText) && functionCalls.Count == 0)
    {
        Console.WriteLine("   ⚠ EMPTY RESPONSE — model does NOT support native tool calling!");
        Console.WriteLine("   → Set NativeToolCalling = false in config");
        return false; // Explicit failure — this is the known issue
    }

    if (functionCalls.Count > 0)
        Console.WriteLine("   ✓ Model used native function calling!");
    else
        Console.WriteLine("   ⚠ Model responded with text but did not use function calling");

    return true;
});

// ═══════════════════════════════════════════
// Test 5: Text-based Tool Calling (system prompt only)
// ═══════════════════════════════════════════
await RunTestAsync("5. Text-based Tool Calling (system prompt)", async () =>
{
    var client = CreateClient(config);

    var systemPrompt = """
        You are a helpful assistant with access to tools.
        To use a tool, output a tool call in this EXACT format:
        <tool_call>
        {"name": "tool_name", "arguments": {"param1": "value1"}}
        </tool_call>

        Available tools:
          - calculator: Evaluates a math expression. Parameters: {"expression": "string"}

        Example:
        <tool_call>
        {"name": "calculator", "arguments": {"expression": "2+2"}}
        </tool_call>

        You MUST use <tool_call> tags to call tools. Do NOT just give the answer.
        """;

    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, "Use the calculator tool to compute 2 + 2.")
    };

    var options = new ChatOptions
    {
        Temperature = 0.1f,
        MaxOutputTokens = 500
    };

    if (config.LmStudio.Model is not null)
        options.ModelId = config.LmStudio.Model;

    var parts = new List<string>();
    var tokenCount = 0;

    await foreach (var update in client.GetStreamingResponseAsync(messages, options))
    {
        foreach (var content in update.Contents)
        {
            if (content is TextContent tc)
            {
                tokenCount++;
                parts.Add(tc.Text);
            }
        }
    }

    var fullText = string.Join("", parts);
    Console.WriteLine($"   Tokens: {tokenCount}, Text length: {fullText.Length}");

    if (string.IsNullOrWhiteSpace(fullText))
        throw new Exception("Empty response");

    // Check for tool call tags
    var hasToolCall = fullText.Contains("<tool_call>", StringComparison.OrdinalIgnoreCase);
    var hasThink = fullText.Contains("<think>", StringComparison.OrdinalIgnoreCase);

    Console.WriteLine($"   Contains <think>: {hasThink}");
    Console.WriteLine($"   Contains <tool_call>: {hasToolCall}");

    // Show relevant portions
    if (hasThink)
    {
        var thinkEnd = fullText.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkEnd > 0)
        {
            var afterThink = fullText[(thinkEnd + 8)..].Trim();
            Console.WriteLine($"   After </think> ({afterThink.Length} chars): {Truncate(afterThink, 300)}");
        }
        else
        {
            Console.WriteLine("   ⚠ <think> tag opened but never closed");
        }
    }

    if (hasToolCall)
    {
        Console.WriteLine("   ✓ Model used <tool_call> tags!");
    }
    else
    {
        // Check for bare JSON tool calls (model outputs JSON without tags)
        var hasBareJson = fullText.Contains("\"name\"") && fullText.Contains("\"arguments\"");
        var hasCodeBlock = fullText.Contains("```json") || fullText.Contains("```");

        if (hasBareJson && !hasCodeBlock)
        {
            Console.WriteLine("   ⚠ Model output bare JSON tool call (no tags, no code blocks)");
            Console.WriteLine("   → OpenOrca's ParseToolCallsFromText Pattern 5 should catch this");

            // Verify by trying to parse the after-think portion
            if (hasThink)
            {
                var thinkEnd2 = fullText.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
                if (thinkEnd2 > 0)
                {
                    var afterThink2 = fullText[(thinkEnd2 + 8)..].Trim();
                    try
                    {
                        var doc = JsonDocument.Parse(afterThink2);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("name", out var n) && root.TryGetProperty("arguments", out _))
                        {
                            Console.WriteLine($"   ✓ Bare JSON is valid and parseable: name={n.GetString()}");
                            Console.WriteLine("   → This WILL work in OpenOrca (Pattern 5 bare JSON match)");
                        }
                    }
                    catch
                    {
                        Console.WriteLine("   ⚠ Bare JSON after </think> is not valid JSON");
                    }
                }
            }
        }
        else if (hasCodeBlock && hasBareJson)
        {
            Console.WriteLine("   ⚠ Model used code blocks with JSON instead of <tool_call> tags");
            Console.WriteLine("   → Nudge mechanism needed to convert code block JSON to tool calls");
        }
        else
        {
            Console.WriteLine("   ⚠ Model did NOT produce tool calls in any format");
        }

        // Print the last 500 chars for diagnosis
        Console.WriteLine($"   Full response (last 500): {Truncate(fullText, 500, fromEnd: true)}");
    }

    // Pass if model used tags OR bare JSON (our parser handles both)
    return hasToolCall || (fullText.Contains("\"name\"") && fullText.Contains("\"arguments\""));
});

// ═══════════════════════════════════════════
// Test 6: Code-block Nudge Detection
// ═══════════════════════════════════════════
await RunTestAsync("6. Code-block Nudge (re-prompt after markdown)", async () =>
{
    var client = CreateClient(config);

    var systemPrompt = """
        You are a helpful assistant with access to tools.
        To use a tool, output a tool call in this EXACT format:
        <tool_call>
        {"name": "tool_name", "arguments": {"param1": "value1"}}
        </tool_call>

        Available tools:
          - write_file: Creates or overwrites a file. Parameters: {"path": "string", "content": "string"}

        IMPORTANT: Do NOT just show code. Use the write_file tool with <tool_call> tags.
        """;

    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, "Create a file called hello.txt containing 'Hello World'")
    };

    var options = new ChatOptions
    {
        Temperature = 0.1f,
        MaxOutputTokens = 500
    };

    if (config.LmStudio.Model is not null)
        options.ModelId = config.LmStudio.Model;

    // First turn
    var parts = new List<string>();
    await foreach (var update in client.GetStreamingResponseAsync(messages, options))
    {
        foreach (var content in update.Contents)
        {
            if (content is TextContent tc)
                parts.Add(tc.Text);
        }
    }

    var firstResponse = string.Join("", parts);
    Console.WriteLine($"   First response ({firstResponse.Length} chars)");

    var hasToolCall = firstResponse.Contains("<tool_call>", StringComparison.OrdinalIgnoreCase);
    if (hasToolCall)
    {
        Console.WriteLine("   ✓ Model used <tool_call> on first try — nudge not needed");
        return true;
    }

    // Model didn't use tool_call — try nudging
    Console.WriteLine("   Model didn't use <tool_call> — applying nudge...");

    messages.Add(new ChatMessage(ChatRole.Assistant, firstResponse));
    messages.Add(new ChatMessage(ChatRole.User,
        "You showed code/text but did not use the <tool_call> tags. " +
        "Please re-do your response using <tool_call> tags to actually execute the action. " +
        "Example:\n<tool_call>\n{\"name\": \"write_file\", \"arguments\": {\"path\": \"hello.txt\", \"content\": \"Hello World\"}}\n</tool_call>"));

    parts.Clear();
    await foreach (var update in client.GetStreamingResponseAsync(messages, options))
    {
        foreach (var content in update.Contents)
        {
            if (content is TextContent tc)
                parts.Add(tc.Text);
        }
    }

    var nudgedResponse = string.Join("", parts);
    Console.WriteLine($"   Nudged response ({nudgedResponse.Length} chars)");

    var hasToolCallAfterNudge = nudgedResponse.Contains("<tool_call>", StringComparison.OrdinalIgnoreCase);
    if (hasToolCallAfterNudge)
    {
        Console.WriteLine("   ✓ Nudge worked! Model used <tool_call> on second attempt");
        Console.WriteLine($"   Response: {Truncate(nudgedResponse, 300)}");
    }
    else
    {
        Console.WriteLine("   ✗ Nudge did NOT work — model still won't use <tool_call>");
        Console.WriteLine($"   Response: {Truncate(nudgedResponse, 300)}");
    }

    return hasToolCallAfterNudge;
});

// ═══════════════════════════════════════════
// Test 7: Realistic scenario (full system prompt + all tools + real user prompt)
// ═══════════════════════════════════════════
await RunTestAsync("7. Realistic: full system prompt + all tools + real prompt", async () =>
{
    var client = CreateClient(config);

    // Build the real tool set via the registry (same as the app does)
    var toolRegistry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
    toolRegistry.DiscoverTools(typeof(ToolRegistry).Assembly);
    var aiTools = toolRegistry.GenerateAITools();
    Console.WriteLine($"   Registered {aiTools.Count} tools");
    if (aiTools.Count != 31)
        Console.WriteLine($"   WARNING: Expected 31 tools but found {aiTools.Count}!");

    // Use the real system prompt (abbreviated but same structure as ReplLoop.GetSystemPrompt)
    var toolList = string.Join("\n", aiTools.OfType<AIFunction>().Select(t => $"  - {t.Name}: {t.Description}"));
    var cwd = Directory.GetCurrentDirectory();
    var platform = Environment.OSVersion.ToString();
    var toolCallExample = """{"name": "tool_name", "arguments": {"param1": "value1"}}""";
    var systemPrompt =
        "You are OpenOrca, an autonomous AI coding agent running in a CLI terminal.\n" +
        "You DO NOT just explain how to do things — you DO them directly using your tools.\n\n" +
        "RESPONSE FORMAT:\n" +
        "If you want to think/reason, use <think>...</think> tags.\n" +
        "After thinking, you MUST output <tool_call> tags to take action.\n\n" +
        "HOW TO CALL TOOLS:\n" +
        "<tool_call>\n" +
        toolCallExample + "\n" +
        "</tool_call>\n\n" +
        "CRITICAL RULES:\n" +
        "- When the user asks you to create a file, USE the write_file tool.\n" +
        "- When the user asks you to run a command, USE the bash tool to run it.\n" +
        "- ALWAYS take action with tools. NEVER just describe what you would do.\n\n" +
        "ENVIRONMENT:\n" +
        $"- Working directory: {cwd}\n" +
        $"- Platform: {platform}\n\n" +
        "AVAILABLE TOOLS:\n" +
        toolList;

    Console.WriteLine($"   System prompt: {systemPrompt.Length} chars");

    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, "can we create a new folder and in that folder, write a python tetris clone")
    };

    // Test A: with native tools (like the real app when nativeToolCalling=true)
    Console.WriteLine("   --- Subtest A: native tools enabled ---");
    var optionsNative = new ChatOptions
    {
        Temperature = config.LmStudio.Temperature,
        MaxOutputTokens = config.LmStudio.MaxTokens,
        Tools = aiTools
    };
    if (config.LmStudio.Model is not null)
        optionsNative.ModelId = config.LmStudio.Model;

    var partsA = new List<string>();
    var funcCallsA = new List<string>();
    await foreach (var update in client.GetStreamingResponseAsync(messages, optionsNative))
    {
        foreach (var content in update.Contents)
        {
            if (content is TextContent tc) partsA.Add(tc.Text);
            else if (content is FunctionCallContent fcc) funcCallsA.Add(fcc.Name);
        }
    }
    var textA = string.Join("", partsA);
    Console.WriteLine($"   Native: {textA.Length} chars text, {funcCallsA.Count} function calls");
    if (funcCallsA.Count > 0)
        Console.WriteLine($"   Native function calls: {string.Join(", ", funcCallsA)}");
    if (textA.Length > 0)
        Console.WriteLine($"   Native text (first 200): {Truncate(textA, 200)}");

    var nativeOk = textA.Length > 0 || funcCallsA.Count > 0;
    if (!nativeOk)
    {
        var serverError = await ProbeServerErrorAsync(config,
            [new { role = "user", content = "hi" }]);
        if (serverError is not null)
            Console.WriteLine($"   Server error: {serverError[..Math.Min(serverError.Length, 300)]}");
        else
            Console.WriteLine($"   ⚠ Genuinely empty — model returned nothing with {aiTools.Count} native tools");
    }

    // Test B: without native tools (text-based only)
    Console.WriteLine("   --- Subtest B: native tools disabled (text-based only) ---");
    var optionsText = new ChatOptions
    {
        Temperature = config.LmStudio.Temperature,
        MaxOutputTokens = config.LmStudio.MaxTokens,
        Tools = [] // no native tools
    };
    if (config.LmStudio.Model is not null)
        optionsText.ModelId = config.LmStudio.Model;

    var partsB = new List<string>();
    await foreach (var update in client.GetStreamingResponseAsync(messages, optionsText))
    {
        foreach (var content in update.Contents)
        {
            if (content is TextContent tc) partsB.Add(tc.Text);
        }
    }
    var textB = string.Join("", partsB);
    Console.WriteLine($"   Text-only: {textB.Length} chars");
    if (textB.Length > 0)
        Console.WriteLine($"   Text-only (first 300): {Truncate(textB, 300)}");

    var textOk = textB.Length > 0;
    if (!textOk)
    {
        var serverError = await ProbeServerErrorAsync(config,
            [new { role = "system", content = systemPrompt },
             new { role = "user", content = "can we create a new folder and in that folder, write a python tetris clone" }]);
        if (serverError is not null)
            Console.WriteLine($"   Server error: {serverError[..Math.Min(serverError.Length, 300)]}");
        else
            Console.WriteLine($"   ⚠ Genuinely empty — model returned nothing even without native tools");
    }

    // Test C: minimal system prompt + native tools (isolate if prompt size is the issue)
    if (!nativeOk && !textOk)
    {
        Console.WriteLine("   --- Subtest C: minimal system prompt + native tools ---");
        var minimalMessages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful coding assistant. Use tools when needed."),
            new(ChatRole.User, "can we create a new folder and in that folder, write a python tetris clone")
        };

        var partsC = new List<string>();
        var funcCallsC = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync(minimalMessages, optionsNative))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent tc) partsC.Add(tc.Text);
                else if (content is FunctionCallContent fcc) funcCallsC.Add(fcc.Name);
            }
        }
        var textC = string.Join("", partsC);
        Console.WriteLine($"   Minimal+tools: {textC.Length} chars text, {funcCallsC.Count} function calls");
        if (textC.Length > 0 || funcCallsC.Count > 0)
            Console.WriteLine("   → Problem is the large system prompt, not the tools themselves");
        else
            Console.WriteLine("   → Problem persists even with minimal prompt — likely tool definitions cause empty response");
    }

    // Test D: no system prompt at all + no tools (absolute baseline)
    if (!nativeOk && !textOk)
    {
        Console.WriteLine("   --- Subtest D: no system prompt, no tools (baseline) ---");
        var bareMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "can we create a new folder and in that folder, write a python tetris clone")
        };
        var optionsBare = new ChatOptions
        {
            Temperature = config.LmStudio.Temperature,
            MaxOutputTokens = config.LmStudio.MaxTokens,
            Tools = []
        };
        if (config.LmStudio.Model is not null)
            optionsBare.ModelId = config.LmStudio.Model;

        var partsD = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync(bareMessages, optionsBare))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent tc) partsD.Add(tc.Text);
            }
        }
        var textD = string.Join("", partsD);
        Console.WriteLine($"   Bare: {textD.Length} chars");
        if (textD.Length > 0)
            Console.WriteLine("   → Model works without system prompt — system prompt is the issue");
        else
            Console.WriteLine("   → Model fails even bare — fundamental model/server problem");
    }

    if (nativeOk)
    {
        Console.WriteLine("   ✓ Native tools + full prompt works");
        return true;
    }
    if (textOk)
    {
        Console.WriteLine("   ⚠ Only text-based mode works — set NativeToolCalling = false");
        return false;
    }
    Console.WriteLine("   ✗ Both modes failed");
    return false;
});

// ═══════════════════════════════════════════
// Summary
// ═══════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("════════════════════════════════════════════");
Console.WriteLine($"  Results: {passed} passed, {failed} failed, {skipped} skipped");
Console.WriteLine("════════════════════════════════════════════");

if (failed > 0)
{
    Console.WriteLine();
    Console.WriteLine("Recommendations:");
    Console.WriteLine("  - If Test 4 failed (native tools → empty), set NativeToolCalling = false");
    Console.WriteLine("  - If Test 5 failed (no <tool_call>), the nudge mechanism in Test 6 may help");
    Console.WriteLine("  - If Test 6 also failed, consider trying a different/larger model");
}

return failed > 0 ? 1 : 0;

// ═══════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════

IChatClient CreateClient(OrcaConfig cfg)
{
    var credential = new ApiKeyCredential(cfg.LmStudio.ApiKey);
    var options = new OpenAIClientOptions
    {
        Endpoint = new Uri(cfg.LmStudio.BaseUrl)
    };
    var model = cfg.LmStudio.Model ?? "default";
    return new OpenAIClient(credential, options).GetChatClient(model).AsIChatClient();
}

/// <summary>
/// Makes a raw HTTP request to the LLM server to get the actual error body,
/// which the OpenAI SDK strips from its exceptions.
/// </summary>
async Task<string?> ProbeServerErrorAsync(OrcaConfig cfg, object[] msgs, string? model = null)
{
    try
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {cfg.LmStudio.ApiKey}");

        var payload = new
        {
            model = model ?? cfg.LmStudio.Model ?? "default",
            messages = msgs,
            max_tokens = 50,
            temperature = 0.1f,
            stream = false
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(
            $"{cfg.LmStudio.BaseUrl.TrimEnd('/')}/chat/completions", content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var errorProp))
                {
                    return errorProp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? errorProp.GetString()
                        : errorProp.TryGetProperty("message", out var msgProp)
                            ? msgProp.GetString()
                            : body;
                }
            }
            catch { /* not JSON */ }
            return body;
        }
        return null;
    }
    catch (Exception ex)
    {
        return $"Probe failed: {ex.Message}";
    }
}

async Task RunTestAsync(string name, Func<Task<bool>> test)
{
    Console.WriteLine($"─── {name} ───");
    try
    {
        var result = await test();
        if (result)
        {
            Console.WriteLine($"   ✓ PASSED");
            passed++;
        }
        else
        {
            Console.WriteLine($"   ✗ FAILED");
            failed++;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   ✗ ERROR: {ex.Message}");
        if (ex.InnerException is not null)
            Console.WriteLine($"     Inner: {ex.InnerException.Message}");
        // Surface the actual server error body if this looks like an HTTP error
        if (ex.Message.Contains("400") || ex.Message.Contains("Service request failed") ||
            ex.InnerException?.Message.Contains("400") == true)
        {
            var serverError = await ProbeServerErrorAsync(config,
                [new { role = "user", content = "hi" }]);
            if (serverError is not null)
                Console.WriteLine($"     Server: {serverError[..Math.Min(serverError.Length, 300)]}");
        }
        failed++;
    }
    Console.WriteLine();
}

string Truncate(string text, int max, bool fromEnd = false)
{
    text = text.ReplaceLineEndings(" ").Trim();
    if (text.Length <= max) return text;
    return fromEnd
        ? "..." + text[^(max - 3)..]
        : text[..(max - 3)] + "...";
}
