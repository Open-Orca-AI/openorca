using Spectre.Console;

namespace OpenOrca.Cli.Rendering;

public sealed class StreamingRenderer
{
    private string _buffer = "";

    public void Clear()
    {
        _buffer = "";
    }

    public void AppendToken(string token)
    {
        _buffer += token;
        // Write raw text directly — never parse as Spectre markup
        Console.Write(token);
    }

    public void Finish()
    {
        if (_buffer.Length > 0 && !_buffer.EndsWith('\n'))
            AnsiConsole.WriteLine();
        _buffer = "";
    }

    public string GetBuffer() => _buffer;
}
