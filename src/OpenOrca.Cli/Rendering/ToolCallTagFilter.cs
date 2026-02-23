namespace OpenOrca.Cli.Rendering;

/// <summary>
/// Stream-time filter that suppresses &lt;tool_call&gt;...&lt;/tool_call&gt; and
/// &lt;|tool_call|&gt;...&lt;|/tool_call|&gt; tags from visible output.
/// Processes tokens one at a time as they arrive from the streaming response.
/// </summary>
public sealed class ToolCallTagFilter
{
    private enum State { Normal, Buffering, Suppressing }

    private static readonly string[] OpenTags = ["<tool_call>", "<|tool_call|>"];
    private static readonly string[] CloseTags = ["</tool_call>", "<|/tool_call|>"];

    private State _state = State.Normal;
    private string _buffer = "";
    private int _activeTagIndex = -1;

    /// <summary>
    /// Process a streaming token and return only the text that should be displayed.
    /// Tool call tag content is silently suppressed.
    /// </summary>
    public string Filter(string token)
    {
        if (string.IsNullOrEmpty(token))
            return token;

        return _state switch
        {
            State.Normal => ProcessNormal(token),
            State.Buffering => ProcessBuffering(token),
            State.Suppressing => ProcessSuppressing(token),
            _ => token
        };
    }

    private string ProcessNormal(string token)
    {
        var ltIdx = token.IndexOf('<');
        if (ltIdx < 0)
            return token;

        // Text before '<' is safe to output, rest goes to buffer for tag detection
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

    /// <summary>
    /// Check the buffer against known open tags. Returns any text safe to display.
    /// </summary>
    private string ResolveBuffer()
    {
        for (var i = 0; i < OpenTags.Length; i++)
        {
            // Full match — transition to suppressing
            if (_buffer.StartsWith(OpenTags[i], StringComparison.OrdinalIgnoreCase))
            {
                _state = State.Suppressing;
                _activeTagIndex = i;
                var remainder = _buffer[OpenTags[i].Length..];
                _buffer = "";
                return ProcessSuppressing(remainder);
            }

            // Partial match — keep buffering
            if (OpenTags[i].StartsWith(_buffer, StringComparison.OrdinalIgnoreCase))
                return "";
        }

        // No match — flush buffer as visible text, but re-scan for more '<' characters
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

    private string ProcessSuppressing(string token)
    {
        _buffer += token;
        var closeTag = CloseTags[_activeTagIndex];

        var closeIdx = _buffer.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
        if (closeIdx >= 0)
        {
            // Found close tag — discard everything up to and including it
            var remainder = _buffer[(closeIdx + closeTag.Length)..];
            _buffer = "";
            _state = State.Normal;
            _activeTagIndex = -1;

            // Process remainder as normal text (may contain more visible text or tags)
            return remainder.Length > 0 ? ProcessNormal(remainder) : "";
        }

        // No close tag yet — keep suppressing, but cap buffer to avoid unbounded growth.
        // Keep enough trailing chars to detect a close tag split across tokens.
        if (_buffer.Length > closeTag.Length * 2)
            _buffer = _buffer[^closeTag.Length..];

        return "";
    }
}
