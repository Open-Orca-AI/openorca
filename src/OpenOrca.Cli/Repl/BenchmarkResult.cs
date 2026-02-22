namespace OpenOrca.Cli.Repl;

internal sealed class BenchmarkRunResult
{
    public long LlmTimeMs { get; set; }
    public long PythonExecTimeMs { get; set; }
    public bool? OutputCorrect { get; set; }
    public string? Notes { get; set; }
    public string? TempFolder { get; set; }
}

internal sealed class BenchmarkResult
{
    public required string ModelName { get; init; }
    public List<BenchmarkRunResult> Runs { get; } = [];
}
