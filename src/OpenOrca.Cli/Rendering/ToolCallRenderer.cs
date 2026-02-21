using Spectre.Console;

namespace OpenOrca.Cli.Rendering;

public sealed class ToolCallRenderer
{
    public void RenderToolCall(string toolName, string arguments)
    {
        var panel = new Panel(Markup.Escape(arguments))
        {
            Header = new PanelHeader($"[yellow] \U0001F40B Tool: {Markup.Escape(toolName)} [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };

        AnsiConsole.Write(panel);
    }

    public void RenderToolResult(string toolName, string result, bool isError = false, TimeSpan? elapsed = null)
    {
        var color = isError ? "red" : "green";
        var icon = isError ? "\u2717" : "\u2713";
        var maxLen = CliConstants.ToolResultDisplayMaxChars;
        var display = result.Length > maxLen
            ? result[..maxLen] + $"\n... ({result.Length - maxLen} chars truncated)"
            : result;

        var elapsedStr = elapsed.HasValue ? $" ({FormatElapsed(elapsed.Value)})" : "";

        var panel = new Panel(Markup.Escape(display))
        {
            Header = new PanelHeader($"[{color}] {icon} Result: {Markup.Escape(toolName)}{elapsedStr} [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(isError ? Color.Red : Color.Green)
        };

        AnsiConsole.Write(panel);
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s"
            : $"{elapsed.TotalSeconds:F1}s";
    }

    public void RenderPermissionPrompt(string toolName, string riskLevel)
    {
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
        var panel = new Panel($"[grey]Tool [bold]{Markup.Escape(toolName)}[/] blocked in plan mode (risk: {Markup.Escape(riskLevel)})[/]")
        {
            Header = new PanelHeader($"[cyan] Plan Mode [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan1)
        };

        AnsiConsole.Write(panel);
    }
}
