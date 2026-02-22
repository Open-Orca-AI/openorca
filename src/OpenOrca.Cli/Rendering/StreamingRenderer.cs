using Spectre.Console;

namespace OpenOrca.Cli.Rendering;

public sealed class StreamingRenderer
{
    private string _buffer = "";

    /// <summary>
    /// When true, tokens are buffered but not written to the console. Used by benchmark mode.
    /// </summary>
    public bool Suppressed { get; set; }

    public void Clear()
    {
        _buffer = "";
    }

    public void AppendToken(string token)
    {
        _buffer += token;
        if (!Suppressed)
            Console.Write(token);
    }

    public void Finish()
    {
        if (!Suppressed && _buffer.Length > 0 && !_buffer.EndsWith('\n'))
            AnsiConsole.WriteLine();
        _buffer = "";
    }

    public string GetBuffer() => _buffer;
}
