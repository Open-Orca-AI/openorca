using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Interactive;

public sealed class AskUserTool : IOrcaTool
{
    public string Name => "ask_user";
    public string Description => "Ask the user a multiple-choice question. Use when you need clarification, want user confirmation before a risky action, or need them to choose between approaches. 2-10 options.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "question": {
                "type": "string",
                "description": "The question to ask the user"
            },
            "options": {
                "type": "array",
                "items": { "type": "string" },
                "description": "2-10 choices for the user to select from"
            }
        },
        "required": ["question", "options"]
    }
    """).RootElement;

    /// <summary>
    /// Callback wired in Program.cs to present the question via Spectre.Console.
    /// Parameters: question, options list, cancellation token. Returns the selected option text.
    /// </summary>
    public Func<string, List<string>, CancellationToken, Task<string>>? UserPrompter { get; set; }

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var question = args.GetProperty("question").GetString()!;
        var optionsElement = args.GetProperty("options");

        // Local models sometimes serialize the array as a JSON string — parse it
        if (optionsElement.ValueKind == JsonValueKind.String)
        {
            var raw = optionsElement.GetString()!;
            try { optionsElement = JsonDocument.Parse(raw).RootElement; }
            catch { return ToolResult.Error("options must be a JSON array of strings."); }
        }

        if (optionsElement.ValueKind != JsonValueKind.Array)
            return ToolResult.Error("options must be a JSON array of strings.");

        var options = new List<string>();
        foreach (var item in optionsElement.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                options.Add(value);
        }

        if (options.Count < 2)
            return ToolResult.Error("At least 2 options are required.");

        if (options.Count > 10)
            return ToolResult.Error("A maximum of 10 options is allowed.");

        if (UserPrompter is null)
            return ToolResult.Error("User prompting not configured.");

        try
        {
            var result = await UserPrompter(question, options, ct);
            return ToolResult.Success(result);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to get user input: {ex.Message}");
        }
    }
}
