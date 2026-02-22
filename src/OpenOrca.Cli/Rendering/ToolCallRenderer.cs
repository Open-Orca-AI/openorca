using System.Text.Json;
using Spectre.Console;

namespace OpenOrca.Cli.Rendering;

public sealed class ToolCallRenderer
{
    /// <summary>
    /// When true, all rendering is suppressed. Used by benchmark mode.
    /// </summary>
    public bool Suppressed { get; set; }

    public void RenderToolCall(string toolName, string arguments)
    {
        if (Suppressed) return;

        var summary = ExtractSummary(toolName, arguments);
        var maxLen = CliConstants.ToolCallSummaryMaxChars;
        if (summary.Length > maxLen)
            summary = summary[..maxLen] + "…";

        var emoji = GetToolEmoji(toolName);
        AnsiConsole.MarkupLine($"  {emoji} [yellow]{Markup.Escape(toolName)}[/] [dim]{Markup.Escape(summary)}[/]");
    }

    public void RenderToolResult(string toolName, string result, bool isError = false, TimeSpan? elapsed = null)
    {
        if (Suppressed) return;

        // Success: silent — show nothing
        if (!isError)
            return;

        // Error: compact one-liner in red, truncated
        var maxLen = CliConstants.ToolErrorDisplayMaxChars;
        var firstLine = result.Split('\n', 2)[0];
        if (firstLine.Length > maxLen)
            firstLine = firstLine[..maxLen] + "…";

        AnsiConsole.MarkupLine($"  [red]✗ {Markup.Escape(toolName)}: {Markup.Escape(firstLine)}[/]");
    }

    public void RenderPermissionPrompt(string toolName, string riskLevel)
    {
        if (Suppressed) return;
        AnsiConsole.MarkupLine($"[yellow]⚠ Tool [bold]{Markup.Escape(toolName)}[/] requires approval (risk: {Markup.Escape(riskLevel)})[/]");
    }

    public void RenderPlanModeToggle(bool enabled)
    {
        if (enabled)
            AnsiConsole.MarkupLine("[cyan]⏸ Plan mode enabled[/] — the model will plan without making changes.");
        else
            AnsiConsole.MarkupLine("[green]▶ Plan mode disabled[/] — the model can now execute changes.");
    }

    public void RenderPlanToolBlocked(string toolName, string riskLevel)
    {
        AnsiConsole.MarkupLine($"  [cyan]⏸ {Markup.Escape(toolName)}[/] [dim]blocked in plan mode (risk: {Markup.Escape(riskLevel)})[/]");
    }

    private static string GetToolEmoji(string toolName) => toolName switch
    {
        "read_file" or "head_file" or "tail_file" => "\U0001f4d6",  // 📖
        "write_file" => "\u270f\ufe0f",                              // ✏️
        "edit_file" or "multi_edit" => "\U0001f4dd",                 // 📝
        "delete_file" => "\U0001f5d1\ufe0f",                        // 🗑️
        "file_info" => "\U0001f4c4",                                 // 📄
        "bash" or "run_command" => "\U0001f4bb",                     // 💻
        "start_background_process" => "\u2699\ufe0f",                // ⚙️
        "glob" or "grep" or "search_text" => "\U0001f50d",          // 🔍
        "git" or "git_status" or "git_diff" or "git_log"
            or "git_commit" => "\U0001f33f",                         // 🌿
        "http_request" => "\U0001f310",                              // 🌐
        "web_search" => "\U0001f50e",                                // 🔎
        "spawn_agent" => "\U0001f916",                               // 🤖
        "list_directory" => "\U0001f4c2",                            // 📂
        "create_directory" or "mkdir" => "\U0001f4c1",               // 📁
        "move_file" or "rename_file" => "\U0001f4e6",                // 📦
        "copy_file" => "\U0001f4cb",                                 // 📋
        "archive" or "extract" => "\U0001f4e5",                      // 📥
        "download_file" => "\u2b07\ufe0f",                           // ⬇️
        _ when toolName.StartsWith("mcp_") => "\U0001f50c",         // 🔌
        _ => "\u2022"                                                // •
    };

    /// <summary>
    /// Extract the most meaningful argument from the tool's JSON args for display.
    /// </summary>
    private static string ExtractSummary(string toolName, string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson) || argsJson == "{}")
            return "";

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            return toolName switch
            {
                // File tools — show path
                "read_file" or "write_file" or "edit_file" or "delete_file"
                    or "file_info" or "head_file" or "tail_file"
                    => TryGetString(root, "path"),

                // Shell
                "bash" or "run_command"
                    => TryGetString(root, "command"),

                // Search tools
                "glob" => TryGetString(root, "pattern"),
                "grep" or "search_text" => TryGetString(root, "pattern") is { Length: > 0 } p
                    ? p + (TryGetString(root, "path") is { Length: > 0 } gp ? " " + gp : "")
                    : TryGetString(root, "path"),

                // Git
                "git" => TryGetString(root, "args"),

                // Web
                "http_request" => TryGetString(root, "url"),
                "web_search" => TryGetString(root, "query"),

                // Agent spawning
                "spawn_agent" => TryGetString(root, "agent_type") is { Length: > 0 } at
                    ? $"[{at}] " + TryGetString(root, "task")
                    : TryGetString(root, "task"),

                // Fallback: first string property value
                _ => GetFirstStringValue(root)
            };
        }
        catch
        {
            return "";
        }
    }

    private static string TryGetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? ""
            : "";
    }

    private static string GetFirstStringValue(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return "";

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString() ?? "";
        }

        return "";
    }
}
