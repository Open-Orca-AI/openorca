using System.Diagnostics;
using OpenOrca.Cli.Serialization;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Input mode for the REPL prompt, cycled via Shift+Tab.
/// </summary>
public enum InputMode
{
    Normal,
    Plan,
    Ask
}

/// <summary>
/// Shared mutable state for the REPL session.
/// Volatile fields ensure visibility across async contexts.
/// </summary>
public sealed class ReplState
{
    private volatile int _verbosity = 1;
    private volatile InputMode _mode;
    private int _totalOutputTokens;
    private int _totalTurns;

    /// <summary>
    /// Verbosity level (0-4). Ctrl+O cycles through levels.
    /// 0 = print only, 1 = tool calls + 7-line preview, 2 = full tool output,
    /// 3 = thinking preview (7 lines), 4 = full thinking.
    /// </summary>
    public int Verbosity { get => _verbosity; set => _verbosity = value; }

    // Derived helpers for readability in rendering code
    public bool ShowPrintOnly => _verbosity == 0;
    public bool ShowToolCalls => _verbosity >= 1;
    public bool ShowFullToolOutput => _verbosity >= 2;
    public bool ShowThinkingPreview => _verbosity >= 3;
    public bool ShowFullThinking => _verbosity >= 4;

    /// <summary>Backward compat for existing code referencing ShowThinking.</summary>
    public bool ShowThinking => _verbosity >= 3;

    /// <summary>
    /// Current input mode (Normal, Plan, Ask). Cycled by Shift+Tab.
    /// </summary>
    public InputMode Mode { get => _mode; set => _mode = value; }

    /// <summary>
    /// Computed property for backward compatibility with existing /plan command and plan mode logic.
    /// </summary>
    public bool PlanMode
    {
        get => _mode == InputMode.Plan;
        set => _mode = value ? InputMode.Plan : InputMode.Normal;
    }

    public int TotalOutputTokens { get => _totalOutputTokens; set => Interlocked.Exchange(ref _totalOutputTokens, value); }
    public int TotalTurns { get => _totalTurns; set => Interlocked.Exchange(ref _totalTurns, value); }
    public Stopwatch SessionStopwatch { get; } = Stopwatch.StartNew();
    public string? LastAssistantResponse { get; set; }
    public string? SuggestedNextPrompt { get; set; }
    public string? CurrentSessionId { get; set; }

    /// <summary>
    /// Tool call history for JSON output mode.
    /// </summary>
    public List<ToolCallRecord> ToolCallHistory { get; } = [];

    /// <summary>
    /// Files modified during this session (for JSON output mode).
    /// </summary>
    public HashSet<string> FilesModified { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cycle to the next input mode: Normal → Plan → Ask → Normal.
    /// </summary>
    public void CycleMode()
    {
        _mode = _mode switch
        {
            InputMode.Normal => InputMode.Plan,
            InputMode.Plan => InputMode.Ask,
            InputMode.Ask => InputMode.Normal,
            _ => InputMode.Normal
        };
    }
}
