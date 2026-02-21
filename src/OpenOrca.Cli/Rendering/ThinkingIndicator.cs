using System.Diagnostics;

namespace OpenOrca.Cli.Rendering;

public sealed class ThinkingIndicator : IDisposable
{
    private static readonly string[] ThinkingWords =
    [
        "Thinking", "Pondering", "Mulling", "Reasoning", "Contemplating",
        "Deliberating", "Cogitating", "Ruminating", "Reflecting", "Musing",
        "Noodling", "Chewing on it", "Brainstorming", "Processing",
        "Figuring it out", "Working it out", "Crunching", "Brewing",
        "Percolating", "Stewing", "Cooking up", "Conjuring",
        "Daydreaming", "Scheming", "Plotting", "Hatching a plan",
        "Deep in thought", "Spinning gears", "Connecting dots",
        "Mind-melding", "Decoding", "Untangling", "Assembling thoughts",
    ];

    // Orca-themed animation frames — a little swimming orca
    private static readonly string[] Frames =
    [
        "   ~•>     ",
        "    ~•>    ",
        "     ~•>   ",
        "      ~•>  ",
        "       ~•> ",
        "      ~•>  ",
        "     ~•>   ",
        "    ~•>    ",
    ];

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _animationTask;
    private readonly TextWriter _output;
    private readonly string _word;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private volatile bool _stopped;
    private volatile int _tokenCount;
    private volatile bool _receivingTokens;
    private volatile int _budgetTokens;

    /// <param name="output">
    /// The TextWriter to render to. Pass the real stdout handle so the
    /// animation is visible even when Console.Out is redirected to a buffer.
    /// </param>
    public ThinkingIndicator(TextWriter? output = null)
    {
        _output = output ?? Console.Out;
        _word = ThinkingWords[Random.Shared.Next(ThinkingWords.Length)];
        _animationTask = Task.Run(AnimateAsync);
    }

    /// <summary>
    /// Thinking token budget. 0 = unlimited.
    /// </summary>
    public int BudgetTokens { get => _budgetTokens; set => _budgetTokens = value; }

    /// <summary>
    /// Call from the streaming loop to update the live token count.
    /// </summary>
    public void UpdateTokenCount(int tokens)
    {
        _tokenCount = tokens;
        _receivingTokens = true;
    }

    private string FormatElapsed()
    {
        var elapsed = _stopwatch.Elapsed;
        return elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s"
            : $"{elapsed.TotalSeconds:F1}s";
    }

    private string BuildStatusLine(int frame)
    {
        var orca = Frames[frame];
        var time = FormatElapsed();

        if (_receivingTokens)
        {
            var tokens = _tokenCount;
            var budget = _budgetTokens;
            var tps = _stopwatch.Elapsed.TotalSeconds > 0.5
                ? $" · {tokens / _stopwatch.Elapsed.TotalSeconds:F1} tok/s"
                : "";
            var tokenDisplay = budget > 0 ? $"{tokens}/{budget} tokens" : $"{tokens} tokens";
            return $"{orca} {_word}... {time} · {tokenDisplay}{tps}";
        }

        return $"{orca} {_word}... {time}";
    }

    private async Task AnimateAsync()
    {
        var frame = 0;
        try
        {
            // Show initial line — write to the real stdout
            _output.Write($"\r\x1b[2m\x1b[36m{BuildStatusLine(0)}\x1b[0m\x1b[K");
            _output.Flush();

            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(120, _cts.Token);
                frame = (frame + 1) % Frames.Length;

                // Move to start of line, clear, redraw
                _output.Write($"\r\x1b[2m\x1b[36m{BuildStatusLine(frame)}\x1b[0m\x1b[K");
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
        _stopwatch.Stop();
        _cts.Cancel();

        try { _animationTask.Wait(500); } catch (AggregateException) { }

        // Clear the thinking line
        _output.Write("\r\x1b[K");
        _output.Flush();
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
