using System.Diagnostics;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Shared mutable state for the REPL session.
/// Volatile fields ensure visibility across async contexts.
/// </summary>
internal sealed class ReplState
{
    private volatile bool _showThinking;
    private volatile bool _planMode;
    private int _totalOutputTokens;
    private int _totalTurns;

    public bool ShowThinking { get => _showThinking; set => _showThinking = value; }
    public bool PlanMode { get => _planMode; set => _planMode = value; }
    public int TotalOutputTokens { get => _totalOutputTokens; set => Interlocked.Exchange(ref _totalOutputTokens, value); }
    public int TotalTurns { get => _totalTurns; set => Interlocked.Exchange(ref _totalTurns, value); }
    public Stopwatch SessionStopwatch { get; } = Stopwatch.StartNew();
    public string? LastAssistantResponse { get; set; }
    public string? CurrentSessionId { get; set; }
}
