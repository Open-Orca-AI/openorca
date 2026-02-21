namespace OpenOrca.Cli.Rendering;

/// <summary>
/// Stream-time state machine that separates &lt;think&gt;...&lt;/think&gt; reasoning
/// from visible response text. Processes tokens one at a time as they arrive
/// from the LLM streaming response.
/// </summary>
public sealed class ThinkTagFilter
{
    private enum State { Detecting, InsideThink, Response }

    /// <summary>Maximum chars to buffer while detecting whether the stream starts with &lt;think&gt;.</summary>
    private const int DetectionLimit = 30;

    private const string OpenTag = "<think>";
    private const string CloseTag = "</think>";

    private State _state = State.Detecting;
    private string _buffer = "";

    /// <summary>
    /// Returns accumulated thinking text (for token counting / Ctrl+O flush).
    /// </summary>
    public string AccumulatedThinking { get; private set; } = "";

    /// <summary>
    /// Whether the filter has transitioned to the Response phase (response tokens are flowing).
    /// </summary>
    public bool InResponsePhase => _state == State.Response;

    /// <summary>
    /// Process a single streaming token and return the thinking/response split.
    /// </summary>
    public (string ThinkingText, string ResponseText) Process(string token)
    {
        return _state switch
        {
            State.Detecting => ProcessDetecting(token),
            State.InsideThink => ProcessInsideThink(token),
            State.Response => ProcessResponse(token),
            _ => ("", token)
        };
    }

    private (string, string) ProcessDetecting(string token)
    {
        _buffer += token;

        // Check if buffer starts with <think> (tolerating leading whitespace)
        var trimmed = _buffer.TrimStart();

        if (trimmed.StartsWith(OpenTag, StringComparison.OrdinalIgnoreCase))
        {
            // Confirmed: model is using think tags
            _state = State.InsideThink;
            // Everything after the open tag is thinking content
            var afterTag = trimmed[OpenTag.Length..];
            AccumulatedThinking += afterTag;

            // Check if close tag is already in the buffer
            return DrainInsideThink();
        }

        // If we have enough non-whitespace content and no "<" at all, it's not a think model
        if (trimmed.Length >= DetectionLimit || (trimmed.Length > 0 && !trimmed.Contains('<')))
        {
            _state = State.Response;
            var response = _buffer;
            _buffer = "";
            return ("", response);
        }

        // Still detecting — hold in buffer, nothing visible yet
        return ("", "");
    }

    private (string, string) ProcessInsideThink(string token)
    {
        _buffer += token;
        return DrainInsideThink();
    }

    /// <summary>
    /// Scan the buffer for &lt;/think&gt;. If found, transition to Response and
    /// return any text after the close tag as response text.
    /// </summary>
    private (string, string) DrainInsideThink()
    {
        var closeIdx = _buffer.IndexOf(CloseTag, StringComparison.OrdinalIgnoreCase);
        if (closeIdx >= 0)
        {
            // Everything before close tag is thinking
            var thinkPart = _buffer[..closeIdx];
            // Everything after close tag is response
            var responsePart = _buffer[(closeIdx + CloseTag.Length)..];

            AccumulatedThinking += thinkPart;
            _buffer = "";
            _state = State.Response;

            return (thinkPart, responsePart);
        }

        // No close tag yet — all buffered content is thinking
        var thinking = _buffer;
        AccumulatedThinking += thinking;
        _buffer = "";
        return (thinking, "");
    }

    private (string, string) ProcessResponse(string token)
    {
        // In response phase, everything is response text
        return ("", token);
    }
}
