namespace OpenOrca.Cli;

/// <summary>
/// Centralized constants for timeouts, truncation limits, and other CLI magic numbers.
/// </summary>
internal static class CliConstants
{
    // Timeouts (seconds)
    public const int BashShortcutTimeoutSeconds = 120;
    public const int HookExecutionTimeoutSeconds = 30;
    public const int HttpProbeTimeoutSeconds = 15;
    public const int StreamingIdleTimeoutSeconds = 120;
    public const int ToolExecutionTimeoutSeconds = 120;
    public const int AgentMaxIterations = 25;

    // Truncation limits (characters)
    public const int BashOutputMaxChars = 5000;
    public const int LogTextMaxChars = 2000;
    public const int SystemPromptDisplayMaxChars = 500;
    public const int ToolResultLogMaxChars = 1000;
    public const int ToolResultDisplayMaxChars = 2000;
    public const int ToolCallSummaryMaxChars = 80;
    public const int ToolErrorDisplayMaxChars = 120;
    public const int CompactMaxOutputTokens = 500;

    // Process wait limits (milliseconds)
    public const int ClipboardProcessWaitMs = 5000;

    // Tool defaults
    public const int DefaultMaxSearchResults = 500;

    // Parallel tool execution
    public const int MaxParallelToolCalls = 8;
}
