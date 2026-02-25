using System.Text.Json;
using OpenOrca.Cli.Repl;
using Spectre.Console;

namespace OpenOrca.Cli.Rendering;

public sealed class ToolCallRenderer
{
    private readonly ReplState _state;
    private readonly IAnsiConsole _console;

    public ToolCallRenderer(ReplState state) : this(state, AnsiConsole.Console) { }

    public ToolCallRenderer(ReplState state, IAnsiConsole console)
    {
        _state = state;
        _console = console;
    }

    /// <summary>
    /// When true, all rendering is suppressed. Used by benchmark mode.
    /// </summary>
    public bool Suppressed { get; set; }

    public void RenderToolCall(string toolName, string arguments)
    {
        if (Suppressed) return;
        if (!_state.ShowToolCalls) return; // Level 0: suppress tool call rendering

        var emoji = GetToolEmoji(toolName);
        var reason = ExtractReason(arguments);
        var description = reason ?? GetToolDescription(toolName, arguments);
        _console.MarkupLine($"  {emoji} [yellow]{Markup.Escape(toolName)}[/] [dim]{Markup.Escape(description)}[/]");

        var summary = ExtractSummary(toolName, arguments);
        if (summary.Length > 0)
        {
            var maxLen = CliConstants.ToolCallSummaryMaxChars;
            if (summary.Length > maxLen)
                summary = summary[..maxLen] + "…";
            _console.MarkupLine($"    [dim]{Markup.Escape(summary)}[/]");
        }
    }

    /// <summary>
    /// Extract the optional _reason field from tool call arguments.
    /// </summary>
    private static string? ExtractReason(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson) || argsJson == "{}")
            return null;

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            return doc.RootElement.TryGetProperty("_reason", out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void RenderToolResult(string toolName, string result, bool isError = false, TimeSpan? elapsed = null)
    {
        if (Suppressed) return;

        if (string.IsNullOrWhiteSpace(result))
            return;

        var isPrint = toolName == "print";

        // At level 0, only print tool output is shown
        if (_state.ShowPrintOnly && !isPrint) return;

        if (isPrint)
        {
            // Print tool: always render prominently (not dim)
            _console.MarkupLine($"  [white]{Markup.Escape(result)}[/]");
            return;
        }

        var lines = result.Split('\n');
        var maxLines = CliConstants.ToolOutputPreviewLines;
        var expanded = _state.ShowFullToolOutput;

        if (isError)
        {
            // Error: red header (first line), then dim preview of remaining lines
            var firstLine = lines[0];
            var headerMaxLen = CliConstants.ToolErrorDisplayMaxChars;
            if (firstLine.Length > headerMaxLen)
                firstLine = firstLine[..headerMaxLen] + "…";

            _console.MarkupLine($"  [red]✗ {Markup.Escape(toolName)}: {Markup.Escape(firstLine)}[/]");

            if (lines.Length > 1)
            {
                var bodyLines = lines[1..];
                RenderPreviewLines(bodyLines, maxLines, expanded);
            }
        }
        else if (toolName is "edit_file" or "multi_edit")
        {
            RenderEditDiffLines(lines, maxLines, expanded);
        }
        else
        {
            // Success: show preview lines in dim
            RenderPreviewLines(lines, maxLines, expanded);
        }
    }

    private void RenderPreviewLines(string[] lines, int maxLines, bool expanded)
    {
        var linesToShow = expanded ? lines.Length : Math.Min(lines.Length, maxLines);

        for (var i = 0; i < linesToShow; i++)
            _console.MarkupLine($"        [dim]{Markup.Escape(lines[i])}[/]");

        if (!expanded && lines.Length > maxLines)
        {
            var remaining = lines.Length - maxLines;
            _console.MarkupLine($"        [dim]⋯ {remaining} more line{(remaining == 1 ? "" : "s")} (Ctrl+O to increase verbosity)[/]");
        }
    }

    private void RenderEditDiffLines(string[] lines, int maxLines, bool expanded)
    {
        var linesToShow = expanded ? lines.Length : Math.Min(lines.Length, maxLines);

        for (var i = 0; i < linesToShow; i++)
        {
            var line = lines[i];
            var escaped = Markup.Escape(line);

            if (line.StartsWith("- ") && line.Contains('│'))
                _console.MarkupLine($"        [#ff9999 on #3d0000]{escaped}[/]");
            else if (line.StartsWith("+ ") && line.Contains('│'))
                _console.MarkupLine($"        [#99ff99 on #003d00]{escaped}[/]");
            else
                _console.MarkupLine($"        [dim]{escaped}[/]");
        }

        if (!expanded && lines.Length > maxLines)
        {
            var remaining = lines.Length - maxLines;
            _console.MarkupLine($"        [dim]⋯ {remaining} more line{(remaining == 1 ? "" : "s")} (Ctrl+O to increase verbosity)[/]");
        }
    }

    public void RenderPermissionPrompt(string toolName, string riskLevel)
    {
        if (Suppressed) return;
        _console.MarkupLine($"[yellow]⚠ Tool [bold]{Markup.Escape(toolName)}[/] requires approval (risk: {Markup.Escape(riskLevel)})[/]");
    }

    public void RenderPlanModeToggle(bool enabled)
    {
        if (enabled)
            _console.MarkupLine("[cyan]⏸ Plan mode enabled[/] — the model will plan without making changes.");
        else
            _console.MarkupLine("[green]▶ Plan mode disabled[/] — the model can now execute changes.");
    }

    public void RenderPlanToolBlocked(string toolName, string riskLevel)
    {
        _console.MarkupLine($"  [cyan]⏸ {Markup.Escape(toolName)}[/] [dim]blocked in plan mode (risk: {Markup.Escape(riskLevel)})[/]");
    }

    private static string GetToolDescription(string toolName, string argsJson)
    {
        // Try to extract a contextual detail from args for richer descriptions
        string? path = null;
        string? pattern = null;
        string? query = null;
        if (!string.IsNullOrWhiteSpace(argsJson) && argsJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(argsJson);
                var root = doc.RootElement;
                path = TryGetString(root, "path") is { Length: > 0 } p ? p : TryGetString(root, "directory");
                pattern = TryGetString(root, "pattern");
                query = TryGetString(root, "query");
            }
            catch { /* JSON parse failed — show generic description */ }
        }

        return toolName switch
        {
            "read_file" => "Read file contents",
            "head_file" => "Read beginning of file",
            "tail_file" => "Read end of file",
            "write_file" => "Write file",
            "edit_file" => "Edit file",
            "multi_edit" => "Edit multiple files",
            "delete_file" => "Delete file",
            "file_info" => "Get file information",
            "list_directory" or "cd" => path is { Length: > 0 }
                ? $"List contents of {Path.GetFileName(path.TrimEnd('/', '\\'))}"
                : "List directory contents",
            "create_directory" or "mkdir" => "Create directory",
            "move_file" or "rename_file" => "Move or rename file",
            "copy_file" => "Copy file",
            "bash" or "run_command" => "Run shell command",
            "start_background_process" => "Start background process",
            "glob" => pattern is { Length: > 0 }
                ? $"Find files matching pattern"
                : "Find files by pattern",
            "grep" or "search_text" => pattern is { Length: > 0 }
                ? "Search file contents for pattern"
                : "Search file contents",
            "git" or "git_status" or "git_diff" or "git_log" or "git_commit" => "Run git operation",
            "http_request" => "Make HTTP request",
            "web_search" => query is { Length: > 0 }
                ? "Search the web"
                : "Search the web",
            "download_file" => "Download file",
            "spawn_agent" => "Spawn sub-agent",
            "archive" or "extract" => "Archive operation",
            _ when toolName.StartsWith("mcp_") => "Call MCP tool",
            _ => "Execute tool"
        };
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
        "list_directory" or "cd" => "\U0001f4c2",                      // 📂
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
