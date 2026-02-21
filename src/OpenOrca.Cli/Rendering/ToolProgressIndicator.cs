using System.Diagnostics;

namespace OpenOrca.Cli.Rendering;

/// <summary>
/// Animated ANSI progress line showing tool execution with a blinking orca emoji.
/// Displays elapsed time per tool and marks tools as completed.
/// </summary>
public sealed class ToolProgressIndicator : IDisposable
{
    private readonly TextWriter _output;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _animationTask;
    private readonly List<ToolState> _tools;
    private volatile bool _stopped;

    private sealed class ToolState
    {
        public string Name { get; init; } = "";
        public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
        public volatile bool Completed;

        /// <summary>Final elapsed time, captured when the tool completes.</summary>
        public TimeSpan FinalElapsed { get; set; }
    }

    public ToolProgressIndicator(TextWriter output, IEnumerable<string> toolNames)
    {
        _output = output;
        _tools = toolNames.Select(n => new ToolState { Name = n }).ToList();
        _animationTask = Task.Run(AnimateAsync);
    }

    /// <summary>
    /// Mark a tool as completed. Its timer freezes and a checkmark replaces the spinner.
    /// </summary>
    public void MarkCompleted(string toolName)
    {
        var tool = _tools.Find(t => t.Name == toolName && !t.Completed);
        if (tool is null) return;
        tool.FinalElapsed = tool.Stopwatch.Elapsed;
        tool.Stopwatch.Stop();
        tool.Completed = true;
    }

    /// <summary>
    /// Returns the elapsed time for a specific tool.
    /// </summary>
    public TimeSpan GetElapsed(string toolName)
    {
        var tool = _tools.Find(t => t.Name == toolName);
        if (tool is null) return TimeSpan.Zero;
        return tool.Completed ? tool.FinalElapsed : tool.Stopwatch.Elapsed;
    }

    private string BuildLine(bool orcaVisible)
    {
        var orca = orcaVisible ? "\U0001F40B" : "  ";
        var parts = _tools.Select(t =>
        {
            var elapsed = t.Completed ? t.FinalElapsed : t.Stopwatch.Elapsed;
            var time = FormatElapsed(elapsed);
            return t.Completed
                ? $"\u2713 {t.Name} ({time})"
                : $"{t.Name} ({time})";
        });

        return $"{orca} {string.Join(" \u00b7 ", parts)}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s"
            : $"{elapsed.TotalSeconds:F1}s";
    }

    private async Task AnimateAsync()
    {
        var frame = 0;
        try
        {
            _output.Write($"\r\x1b[2m\x1b[36m{BuildLine(true)}\x1b[0m\x1b[K");
            _output.Flush();

            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(500, _cts.Token);
                frame++;
                var orcaVisible = frame % 2 == 0;

                _output.Write($"\r\x1b[2m\x1b[36m{BuildLine(orcaVisible)}\x1b[0m\x1b[K");
                _output.Flush();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _cts.Cancel();

        try { _animationTask.Wait(500); } catch (AggregateException) { }

        // Clear the progress line
        _output.Write("\r\x1b[K");
        _output.Flush();
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
