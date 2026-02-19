using Microsoft.Extensions.Logging;
using OpenOrca.Core.Configuration;
using Spectre.Console;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Interactive configuration editing UI using Spectre.Console.
/// </summary>
internal sealed class ConfigEditor
{
    private readonly OrcaConfig _config;
    private readonly ConfigManager _configManager;
    private readonly ILogger _logger;

    public ConfigEditor(OrcaConfig config, ConfigManager configManager, ILogger logger)
    {
        _config = config;
        _configManager = configManager;
        _logger = logger;
    }

    public async Task ShowConfigAsync()
    {
        while (true)
        {
            AnsiConsole.WriteLine();
            var section = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Configuration[/]")
                    .HighlightStyle(Style.Parse("cyan"))
                    .AddChoices(
                        "LM Studio",
                        "Permissions",
                        "Session",
                        "Context",
                        "View All",
                        "Save & Exit",
                        "Exit (discard changes)"));

            switch (section)
            {
                case "LM Studio":
                    await EditLmStudioConfigAsync();
                    break;
                case "Permissions":
                    EditPermissionsConfig();
                    break;
                case "Session":
                    EditSessionConfig();
                    break;
                case "Context":
                    EditContextConfig();
                    break;
                case "View All":
                    RenderConfigTable();
                    break;
                case "Save & Exit":
                    await _configManager.SaveAsync();
                    AnsiConsole.MarkupLine("[green]Configuration saved.[/]");
                    return;
                case "Exit (discard changes)":
                    return;
            }
        }
    }

    private async Task EditLmStudioConfigAsync()
    {
        while (true)
        {
            var lm = _config.LmStudio;

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]LM Studio Settings[/]")
                    .HighlightStyle(Style.Parse("cyan"))
                    .AddChoices(
                        $"Base URL            : {lm.BaseUrl}",
                        $"API Key             : {MaskKey(lm.ApiKey)}",
                        $"Model               : {lm.Model ?? "(auto)"}",
                        $"Temperature         : {lm.Temperature:F1}",
                        $"Max Tokens          : {lm.MaxTokens?.ToString() ?? "(unlimited)"}",
                        $"Timeout (seconds)   : {lm.TimeoutSeconds}",
                        $"Native Tool Calling : {BoolDisplay(lm.NativeToolCalling)}",
                        "Test Connection",
                        "Back"));

            if (choice.StartsWith("Back"))
                return;

            if (choice.StartsWith("Test Connection"))
            {
                await TestConnectionAsync();
                continue;
            }

            if (choice.StartsWith("Base URL"))
            {
                var newUrl = AnsiConsole.Prompt(
                    new TextPrompt<string>("Base URL:")
                        .DefaultValue(lm.BaseUrl));

                if (!Uri.TryCreate(newUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https"))
                {
                    AnsiConsole.MarkupLine("[red]Invalid URL format. Must be http:// or https://[/]");
                }
                else
                {
                    lm.BaseUrl = newUrl;
                }
            }
            else if (choice.StartsWith("API Key"))
            {
                lm.ApiKey = AnsiConsole.Prompt(
                    new TextPrompt<string>("API Key:")
                        .DefaultValue(lm.ApiKey)
                        .Secret('*'));
            }
            else if (choice.StartsWith("Model"))
            {
                // Try to fetch available models for selection
                var models = await FetchModelsAsync();
                if (models.Count > 0)
                {
                    models.Insert(0, "(auto - let LM Studio decide)");
                    var selected = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("Select model:")
                            .HighlightStyle(Style.Parse("cyan"))
                            .AddChoices(models));

                    lm.Model = selected.StartsWith("(auto") ? null : selected;
                }
                else
                {
                    var val = AnsiConsole.Prompt(
                        new TextPrompt<string>("Model name (blank for auto):")
                            .DefaultValue(lm.Model ?? "")
                            .AllowEmpty());
                    lm.Model = string.IsNullOrWhiteSpace(val) ? null : val;
                }
            }
            else if (choice.StartsWith("Temperature"))
            {
                lm.Temperature = AnsiConsole.Prompt(
                    new TextPrompt<float>("Temperature (0.0 - 2.0):")
                        .DefaultValue(lm.Temperature)
                        .Validate(v => v is >= 0f and <= 2f
                            ? ValidationResult.Success()
                            : ValidationResult.Error("Must be between 0.0 and 2.0")));
            }
            else if (choice.StartsWith("Max Tokens"))
            {
                var val = AnsiConsole.Prompt(
                    new TextPrompt<string>("Max tokens (blank for unlimited):")
                        .DefaultValue(lm.MaxTokens?.ToString() ?? "")
                        .AllowEmpty());
                lm.MaxTokens = int.TryParse(val, out var n) ? n : null;
            }
            else if (choice.StartsWith("Timeout"))
            {
                lm.TimeoutSeconds = AnsiConsole.Prompt(
                    new TextPrompt<int>("Timeout (seconds):")
                        .DefaultValue(lm.TimeoutSeconds)
                        .Validate(v => v > 0
                            ? ValidationResult.Success()
                            : ValidationResult.Error("Must be positive")));
            }
            else if (choice.StartsWith("Native Tool Calling"))
            {
                lm.NativeToolCalling = ToggleBool(lm.NativeToolCalling,
                    "Native tool calling (only enable if your model supports OpenAI function calling)");
            }
        }
    }

    private void EditPermissionsConfig()
    {
        while (true)
        {
            var p = _config.Permissions;

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Permissions Settings[/]")
                    .HighlightStyle(Style.Parse("cyan"))
                    .AddChoices(
                        $"Auto-approve read-only : {BoolDisplay(p.AutoApproveReadOnly)}",
                        $"Auto-approve moderate  : {BoolDisplay(p.AutoApproveModerate)}",
                        $"Auto-approve ALL       : {BoolDisplay(p.AutoApproveAll)}",
                        $"Always approve         : [[{Markup.Escape(string.Join(", ", p.AlwaysApprove))}]]",
                        $"Disabled tools         : [[{Markup.Escape(string.Join(", ", p.DisabledTools))}]]",
                        "Back"));

            if (choice.StartsWith("Back"))
                return;

            if (choice.StartsWith("Auto-approve read-only"))
                p.AutoApproveReadOnly = ToggleBool(p.AutoApproveReadOnly, "Auto-approve read-only tools");
            else if (choice.StartsWith("Auto-approve moderate"))
                p.AutoApproveModerate = ToggleBool(p.AutoApproveModerate, "Auto-approve moderate tools");
            else if (choice.StartsWith("Auto-approve ALL"))
                p.AutoApproveAll = ToggleBool(p.AutoApproveAll, "Auto-approve ALL tools (dangerous)");
            else if (choice.StartsWith("Always approve"))
                p.AlwaysApprove = EditStringList("Always-approve tools", p.AlwaysApprove);
            else if (choice.StartsWith("Disabled tools"))
                p.DisabledTools = EditStringList("Disabled tools", p.DisabledTools);
        }
    }

    private void EditSessionConfig()
    {
        while (true)
        {
            var s = _config.Session;

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Session Settings[/]")
                    .HighlightStyle(Style.Parse("cyan"))
                    .AddChoices(
                        $"Auto-save    : {BoolDisplay(s.AutoSave)}",
                        $"Max sessions : {s.MaxSessions}",
                        "Back"));

            if (choice.StartsWith("Back"))
                return;

            if (choice.StartsWith("Auto-save"))
                s.AutoSave = ToggleBool(s.AutoSave, "Auto-save sessions");
            else if (choice.StartsWith("Max sessions"))
            {
                s.MaxSessions = AnsiConsole.Prompt(
                    new TextPrompt<int>("Max sessions:")
                        .DefaultValue(s.MaxSessions)
                        .Validate(v => v > 0
                            ? ValidationResult.Success()
                            : ValidationResult.Error("Must be positive")));
            }
        }
    }

    private void EditContextConfig()
    {
        while (true)
        {
            var ctx = _config.Context;

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Context Settings[/]")
                    .HighlightStyle(Style.Parse("cyan"))
                    .AddChoices(
                        $"Context window size     : {ctx.ContextWindowSize}",
                        $"Auto-compact enabled    : {BoolDisplay(ctx.AutoCompactEnabled)}",
                        $"Auto-compact threshold  : {ctx.AutoCompactThreshold:P0}",
                        $"Preserve last N turns   : {ctx.CompactPreserveLastN}",
                        "Back"));

            if (choice.StartsWith("Back"))
                return;

            if (choice.StartsWith("Context window size"))
            {
                ctx.ContextWindowSize = AnsiConsole.Prompt(
                    new TextPrompt<int>("Context window size (tokens):")
                        .DefaultValue(ctx.ContextWindowSize)
                        .Validate(v => v > 100
                            ? ValidationResult.Success()
                            : ValidationResult.Error("Must be > 100")));
            }
            else if (choice.StartsWith("Auto-compact enabled"))
            {
                ctx.AutoCompactEnabled = ToggleBool(ctx.AutoCompactEnabled, "Auto-compact enabled");
            }
            else if (choice.StartsWith("Auto-compact threshold"))
            {
                ctx.AutoCompactThreshold = AnsiConsole.Prompt(
                    new TextPrompt<float>("Auto-compact threshold (0.0-1.0):")
                        .DefaultValue(ctx.AutoCompactThreshold)
                        .Validate(v => v is > 0f and <= 1f
                            ? ValidationResult.Success()
                            : ValidationResult.Error("Must be between 0.0 and 1.0")));
            }
            else if (choice.StartsWith("Preserve last N"))
            {
                ctx.CompactPreserveLastN = AnsiConsole.Prompt(
                    new TextPrompt<int>("Preserve last N turns during compaction:")
                        .DefaultValue(ctx.CompactPreserveLastN)
                        .Validate(v => v >= 1
                            ? ValidationResult.Success()
                            : ValidationResult.Error("Must be >= 1")));
            }
        }
    }

    private void RenderConfigTable()
    {
        var lm = _config.LmStudio;
        var p = _config.Permissions;
        var s = _config.Session;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .Title("[bold]Current Configuration[/]")
            .AddColumn("[bold]Setting[/]")
            .AddColumn("[bold]Value[/]");

        table.AddRow("[yellow]LM Studio[/]", "");
        table.AddRow("  Base URL", Markup.Escape(lm.BaseUrl));
        table.AddRow("  API Key", MaskKey(lm.ApiKey));
        table.AddRow("  Model", Markup.Escape(lm.Model ?? "(auto)"));
        table.AddRow("  Temperature", lm.Temperature.ToString("F1"));
        table.AddRow("  Max Tokens", lm.MaxTokens?.ToString() ?? "(unlimited)");
        table.AddRow("  Timeout", $"{lm.TimeoutSeconds}s");
        table.AddRow("  Native Tool Calling", BoolDisplay(lm.NativeToolCalling));

        table.AddRow("", "");
        table.AddRow("[yellow]Permissions[/]", "");
        table.AddRow("  Auto-approve read-only", BoolDisplay(p.AutoApproveReadOnly));
        table.AddRow("  Auto-approve moderate", BoolDisplay(p.AutoApproveModerate));
        table.AddRow("  Auto-approve ALL", BoolDisplay(p.AutoApproveAll));
        table.AddRow("  Always approve", Markup.Escape(string.Join(", ", p.AlwaysApprove)));
        table.AddRow("  Disabled tools", Markup.Escape(string.Join(", ", p.DisabledTools)));

        table.AddRow("", "");
        table.AddRow("[yellow]Session[/]", "");
        table.AddRow("  Auto-save", BoolDisplay(s.AutoSave));
        table.AddRow("  Max sessions", s.MaxSessions.ToString());

        var ctx = _config.Context;
        table.AddRow("", "");
        table.AddRow("[yellow]Context[/]", "");
        table.AddRow("  Window size", ctx.ContextWindowSize.ToString());
        table.AddRow("  Auto-compact", BoolDisplay(ctx.AutoCompactEnabled));
        table.AddRow("  Compact threshold", $"{ctx.AutoCompactThreshold:P0}");
        table.AddRow("  Preserve last N", ctx.CompactPreserveLastN.ToString());

        var hooks = _config.Hooks;
        table.AddRow("", "");
        table.AddRow("[yellow]Hooks[/]", "");
        table.AddRow("  Pre-tool hooks", hooks.PreToolHooks.Count > 0 ? string.Join(", ", hooks.PreToolHooks.Keys) : "(none)");
        table.AddRow("  Post-tool hooks", hooks.PostToolHooks.Count > 0 ? string.Join(", ", hooks.PostToolHooks.Keys) : "(none)");

        table.AddRow("", "");
        table.AddRow("[grey]Config file[/]", Markup.Escape(ConfigManager.GetConfigDirectory() + "/config.json"));

        AnsiConsole.Write(table);
    }

    public async Task TestConnectionAsync()
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("yellow"))
            .StartAsync("Testing connection...", async _ =>
            {
                var discovery = new Core.Client.ModelDiscovery(_config,
                    _logger as ILogger<Core.Client.ModelDiscovery>
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Client.ModelDiscovery>.Instance);
                var models = await discovery.GetAvailableModelsAsync();

                if (models.Count > 0)
                {
                    AnsiConsole.MarkupLine($"[green]Connected! Found {models.Count} model(s):[/]");
                    foreach (var m in models)
                        AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(m)}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Could not connect to LM Studio.[/]");
                }
            });
    }

    public async Task<List<string>> FetchModelsAsync()
    {
        try
        {
            var discovery = new Core.Client.ModelDiscovery(_config,
                _logger as ILogger<Core.Client.ModelDiscovery>
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Client.ModelDiscovery>.Instance);
            return await discovery.GetAvailableModelsAsync();
        }
        catch (Exception)
        {
            return [];
        }
    }

    internal static bool ToggleBool(bool current, string label)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"{label}:")
                .HighlightStyle(Style.Parse("cyan"))
                .AddChoices("On", "Off")) == "On";
    }

    internal static List<string> EditStringList(string label, List<string> current)
    {
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold]{label}[/]: [{Markup.Escape(string.Join(", ", current))}]")
                .HighlightStyle(Style.Parse("cyan"))
                .AddChoices("Add item", "Remove item", "Clear all", "Back"));

        switch (action)
        {
            case "Add item":
                var newItem = AnsiConsole.Prompt(
                    new TextPrompt<string>("Tool name to add:"));
                if (!string.IsNullOrWhiteSpace(newItem) && !current.Contains(newItem, StringComparer.OrdinalIgnoreCase))
                    current.Add(newItem);
                break;

            case "Remove item" when current.Count > 0:
                var toRemove = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select item to remove:")
                        .AddChoices(current));
                current.Remove(toRemove);
                break;

            case "Clear all":
                current.Clear();
                break;
        }

        return current;
    }

    internal static string BoolDisplay(bool value) =>
        value ? "[green]On[/]" : "[grey]Off[/]";

    internal static string MaskKey(string key) =>
        key.Length <= 6 ? "****" : key[..3] + new string('*', key.Length - 6) + key[^3..];
}
