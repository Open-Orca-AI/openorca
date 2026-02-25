namespace OpenOrca.Cli.Rendering;

/// <summary>
/// Stream-time filter that captures &lt;next-prompt&gt;...&lt;/next-prompt&gt; content
/// from the LLM response and suppresses it from visible output.
/// The captured text is available via <see cref="CapturedPrompt"/> after streaming ends.
/// </summary>
public sealed class NextPromptTagFilter
{
    private enum State { Normal, Buffering, Inside }

    private const string OpenTag = "<next-prompt>";
    private const string CloseTag = "</next-prompt>";

    private State _state = State.Normal;
    private string _buffer = "";
    private string _captured = "";

    /// <summary>
    /// The captured next-prompt suggestion, or null if none was found.
    /// </summary>
    public string? CapturedPrompt => string.IsNullOrWhiteSpace(_captured) ? null : _captured.Trim();

    /// <summary>
    /// Process a streaming token and return only the text that should be displayed.
    /// Content inside &lt;next-prompt&gt; tags is captured and suppressed.
    /// </summary>
    public string Filter(string token)
    {
        if (string.IsNullOrEmpty(token))
            return token;

        return _state switch
        {
            State.Normal => ProcessNormal(token),
            State.Buffering => ProcessBuffering(token),
            State.Inside => ProcessInside(token),
            _ => token
        };
    }

    private string ProcessNormal(string token)
    {
        var ltIdx = token.IndexOf('<');
        if (ltIdx < 0)
            return token;

        var safe = token[..ltIdx];
        _buffer = token[ltIdx..];
        _state = State.Buffering;
        return safe + ResolveBuffer();
    }

    private string ProcessBuffering(string token)
    {
        _buffer += token;
        return ResolveBuffer();
    }

    private string ResolveBuffer()
    {
        // Full match — transition to capturing (reset any previous capture)
        if (_buffer.StartsWith(OpenTag, StringComparison.OrdinalIgnoreCase))
        {
            _state = State.Inside;
            _captured = "";
            var remainder = _buffer[OpenTag.Length..];
            _buffer = "";
            return ProcessInside(remainder);
        }

        // Partial match — keep buffering
        if (OpenTag.StartsWith(_buffer, StringComparison.OrdinalIgnoreCase))
            return "";

        // No match — flush buffer as visible text, re-scan for more '<'
        _state = State.Normal;
        var flushed = _buffer;
        _buffer = "";

        var nextLt = flushed.IndexOf('<', 1);
        if (nextLt >= 0)
        {
            var safe = flushed[..nextLt];
            _buffer = flushed[nextLt..];
            _state = State.Buffering;
            return safe + ResolveBuffer();
        }

        return flushed;
    }

    private string ProcessInside(string token)
    {
        _buffer += token;

        var closeIdx = _buffer.IndexOf(CloseTag, StringComparison.OrdinalIgnoreCase);
        if (closeIdx >= 0)
        {
            // Found close tag — capture content, return remainder
            _captured += _buffer[..closeIdx];
            var remainder = _buffer[(closeIdx + CloseTag.Length)..];
            _buffer = "";
            _state = State.Normal;

            return remainder.Length > 0 ? ProcessNormal(remainder) : "";
        }

        // No close tag yet — accumulate into captured, keep tail for split-tag detection
        if (_buffer.Length > CloseTag.Length)
        {
            var safeLen = _buffer.Length - CloseTag.Length;
            _captured += _buffer[..safeLen];
            _buffer = _buffer[safeLen..];
        }

        return "";
    }
}
