using System.Diagnostics;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Shared mutable state for the REPL session.
/// </summary>
internal sealed class ReplState
{
    public bool ShowThinking { get; set; }
    public bool PlanMode { get; set; }
    public int TotalOutputTokens { get; set; }
    public int TotalTurns { get; set; }
    public Stopwatch SessionStopwatch { get; } = Stopwatch.StartNew();
    public string? LastAssistantResponse { get; set; }
    public string? CurrentSessionId { get; set; }
}
