using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenOrca.Cli.Rendering;
using OpenOrca.Core.Chat;
using OpenOrca.Core.Client;
using OpenOrca.Core.Configuration;
using Spectre.Console;

namespace OpenOrca.Cli.Repl;

internal sealed class BenchmarkRunner
{
    private const int PythonTimeoutSeconds = 30;
    private const int ModelTimeoutSeconds = 90;
    private const int DefaultRunsPerModel = 3;

    private static readonly int[] ExpectedPrimes =
        [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97];

    private static readonly string[] BenchmarkPrompts =
    [
        "Create a single Python file called main.py in the current directory. It should be a minimal skeleton with a main function and an `if __name__ == '__main__'` block.",
        "Make this python project calculate prime numbers up to 50, writing the output to a file, one result per line, in a file called 'output.txt'.",
        "Update it so it produces prime numbers up to 100."
    ];

    private readonly OrcaConfig _config;
    private readonly SystemPromptBuilder _systemPromptBuilder;
    private readonly ILogger _logger;
    private readonly Func<Conversation, CancellationToken, Task> _runAgentLoop;
    private readonly ToolCallRenderer _toolCallRenderer;
    private readonly StreamingRenderer _streamingRenderer;
    private readonly ReplState _state;

    public BenchmarkRunner(
        OrcaConfig config,
        SystemPromptBuilder systemPromptBuilder,
        ILogger logger,
        Func<Conversation, CancellationToken, Task> runAgentLoop,
        ToolCallRenderer toolCallRenderer,
        StreamingRenderer streamingRenderer,
        ReplState state)
    {
        _config = config;
        _systemPromptBuilder = systemPromptBuilder;
        _logger = logger;
        _runAgentLoop = runAgentLoop;
        _toolCallRenderer = toolCallRenderer;
        _streamingRenderer = streamingRenderer;
        _state = state;
    }

    public async Task RunAsync(int runsPerModel, IReadOnlyList<string>? modelFilter, CancellationToken ct)
    {
        AnsiConsole.MarkupLine($"[bold cyan]Starting model benchmark ({runsPerModel} run(s) per model)...[/]");
        AnsiConsole.WriteLine();

        List<string> models;

        if (modelFilter is null)
        {
            // Benchmark current model only
            var current = _config.LmStudio.Model;
            if (string.IsNullOrWhiteSpace(current))
            {
                AnsiConsole.MarkupLine("[red]No model configured. Set a model with /model or use models=all.[/]");
                return;
            }

            models = [current];
            AnsiConsole.MarkupLine($"[grey]Benchmarking current model: {Markup.Escape(current)}[/]");
        }
        else
        {
            // Discover and filter
            ModelDiscovery.InvalidateCache();
            var discovery = new ModelDiscovery(_config,
                _logger as ILogger<ModelDiscovery>
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ModelDiscovery>.Instance);
            var available = await discovery.GetAvailableModelsAsync(ct);

            if (available.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No models found on the server. Is LM Studio running with models loaded?[/]");
                return;
            }

            if (modelFilter.Count == 0)
            {
                // models=all
                models = available;
            }
            else
            {
                // Match requested models against available ones (case-insensitive)
                models = [];
                foreach (var requested in modelFilter)
                {
                    var match = available.FirstOrDefault(m =>
                        m.Equals(requested, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        models.Add(match);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Model not found: {Markup.Escape(requested)} — skipping[/]");
                    }
                }

                if (models.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]None of the requested models are available.[/]");
                    AnsiConsole.MarkupLine($"[grey]Available: {Markup.Escape(string.Join(", ", available))}[/]");
                    return;
                }
            }

            AnsiConsole.MarkupLine($"[grey]Benchmarking {models.Count} model(s): {Markup.Escape(string.Join(", ", models))}[/]");
        }

        AnsiConsole.WriteLine();

        var originalModel = _config.LmStudio.Model;
        var originalCwd = Directory.GetCurrentDirectory();
        var results = new List<BenchmarkResult>();

        try
        {
            foreach (var model in models)
            {
                ct.ThrowIfCancellationRequested();

                var modelResult = new BenchmarkResult { ModelName = model };

                for (var run = 1; run <= runsPerModel; run++)
                {
                    ct.ThrowIfCancellationRequested();
                    AnsiConsole.Write(new Rule($"[bold yellow]{Markup.Escape(model)} — Run {run}/{runsPerModel}[/]").LeftJustified());
                    var runResult = await BenchmarkSingleRunAsync(model, run, ct);
                    modelResult.Runs.Add(runResult);
                }

                results.Add(modelResult);
            }
        }
        finally
        {
            // Restore original state
            _config.LmStudio.Model = originalModel;
            Directory.SetCurrentDirectory(originalCwd);
        }

        RenderResultsTable(results);
    }

    private async Task<BenchmarkRunResult> BenchmarkSingleRunAsync(string model, int runNumber, CancellationToken ct)
    {
        var sanitized = SanitizeModelName(model);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var tempDir = Path.Combine(Path.GetTempPath(), $"orca-benchmark-{sanitized}-run{runNumber}-{timestamp}");
        Directory.CreateDirectory(tempDir);

        var result = new BenchmarkRunResult();
        var originalCwd = Directory.GetCurrentDirectory();

        // Use a dedicated CTS so we can signal cancellation to the agent loop,
        // plus Task.WhenAny as a hard cutoff in case the agent loop doesn't exit
        // (e.g. bash process ignoring CancellationToken on Windows).
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var workTask = RunBenchmarkCoreAsync(model, runNumber, tempDir, result, runCts.Token);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(ModelTimeoutSeconds), ct);

        var completed = await Task.WhenAny(workTask, timeoutTask);

        if (completed == workTask)
        {
            // Work finished before timeout — propagate any exception
            await workTask;
        }
        else
        {
            // Hard timeout — the agent loop may be stuck. Signal cancellation
            // (best-effort) and move on without awaiting the hung task.
            ct.ThrowIfCancellationRequested(); // Ctrl+C takes priority
            await runCts.CancelAsync();
            result.OutputCorrect = false;
            result.Notes = $"Timed out after {ModelTimeoutSeconds}s";
            AnsiConsole.MarkupLine($"\n[red]Run timed out after {ModelTimeoutSeconds}s — skipping.[/]");
        }

        // Always restore CWD (the core method restores in its finally, but guard
        // against the timeout path where it may still be in the temp dir).
        Directory.SetCurrentDirectory(originalCwd);

        // Clean up temp folder
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Best effort — file locks or permissions may prevent cleanup
        }

        return result;
    }

    private async Task RunBenchmarkCoreAsync(
        string model, int runNumber, string tempDir, BenchmarkRunResult result, CancellationToken ct)
    {
        var originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempDir);
            _config.LmStudio.Model = model;

            // Run LLM prompts
            var llmSw = Stopwatch.StartNew();
            var conversation = new Conversation();
            var systemPrompt = await _systemPromptBuilder.GetSystemPromptAsync(null, false);
            conversation.AddSystemMessage(systemPrompt);

            for (var i = 0; i < BenchmarkPrompts.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var prompt = BenchmarkPrompts[i];
                AnsiConsole.MarkupLine($"  [grey]Prompt {i + 1}/{BenchmarkPrompts.Length}[/]");
                conversation.AddUserMessage(prompt);

                var toolCountBefore = _state.ToolCallHistory.Count;
                var promptSw = Stopwatch.StartNew();

                // Suppress streaming text (model output / XML tool tags) but keep
                // tool call summaries visible so the user sees what the model is doing
                _streamingRenderer.Suppressed = true;
                try
                {
                    await _runAgentLoop(conversation, ct);
                }
                finally
                {
                    _streamingRenderer.Suppressed = false;
                }

                promptSw.Stop();
                var toolCount = _state.ToolCallHistory.Count - toolCountBefore;
                var errorCount = _state.ToolCallHistory.Skip(toolCountBefore).Count(t => t.IsError);
                var errors = errorCount > 0 ? $", [red]{errorCount} error(s)[/]" : "";
                AnsiConsole.MarkupLine($"  [dim]({promptSw.Elapsed.TotalSeconds:F1}s, {toolCount} tool call(s){errors})[/]");
            }

            llmSw.Stop();
            result.LlmTimeMs = llmSw.ElapsedMilliseconds;

            // Find and run Python script
            var entryPoint = FindPythonEntryPoint(tempDir);
            if (entryPoint is null)
            {
                result.Notes = "No Python entry point found";
                AnsiConsole.MarkupLine("[red]No Python entry point found.[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[grey]Running: python {Markup.Escape(Path.GetFileName(entryPoint))}[/]");
            var (exitCode, pythonMs, pythonError) = await RunPythonAsync(entryPoint, ct);
            result.PythonExecTimeMs = pythonMs;

            if (exitCode != 0)
            {
                result.OutputCorrect = false;
                result.Notes = $"Python exited with code {exitCode}: {pythonError}";
                AnsiConsole.MarkupLine($"[red]Python failed (exit {exitCode}): {Markup.Escape(pythonError ?? "")}[/]");
                return;
            }

            // Validate output
            var outputPath = Path.Combine(tempDir, "output.txt");
            if (!File.Exists(outputPath))
            {
                result.OutputCorrect = false;
                result.Notes = "output.txt not found";
                AnsiConsole.MarkupLine("[red]output.txt not found.[/]");
                return;
            }

            var outputContent = await File.ReadAllTextAsync(outputPath, ct);
            var (correct, validationNotes) = ValidateOutput(outputContent);
            result.OutputCorrect = correct;
            result.Notes = validationNotes;

            var statusColor = correct ? "green" : "red";
            var statusText = correct ? "PASS" : "FAIL";
            AnsiConsole.MarkupLine($"[{statusColor}]{statusText}[/] — {Markup.Escape(validationNotes ?? "")}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Notes = $"Error: {ex.Message}";
            _logger.LogError(ex, "Benchmark error for model {Model} run {Run}", model, runNumber);
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
        }
    }

    private static async Task<(int exitCode, long elapsedMs, string? error)> RunPythonAsync(
        string scriptPath, CancellationToken ct)
    {
        var python = OperatingSystem.IsWindows() ? "python" : "python3";
        var psi = new ProcessStartInfo(python, $"\"{scriptPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!
        };

        var sw = Stopwatch.StartNew();
        using var proc = Process.Start(psi);
        if (proc is null)
            return (-1, 0, "Failed to start Python process");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(PythonTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var stderr = await proc.StandardError.ReadToEndAsync(linked.Token);
            await proc.WaitForExitAsync(linked.Token);
            sw.Stop();
            return (proc.ExitCode, sw.ElapsedMilliseconds, stderr.Length > 200 ? stderr[..200] : stderr);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort process kill */ }
            sw.Stop();
            return (-1, sw.ElapsedMilliseconds, $"Timed out after {PythonTimeoutSeconds}s");
        }
    }

    internal static string? FindPythonEntryPoint(string directory)
    {
        var mainPy = Path.Combine(directory, "main.py");
        if (File.Exists(mainPy))
            return mainPy;

        var pyFiles = Directory.GetFiles(directory, "*.py")
            .Where(f =>
            {
                var name = Path.GetFileName(f).ToLowerInvariant();
                return name != "test.py"
                    && !name.StartsWith("test_")
                    && !name.EndsWith("_test.py")
                    && name != "setup.py"
                    && name != "conftest.py";
            })
            .ToArray();

        return pyFiles.Length == 1 ? pyFiles[0] : null;
    }

    internal static (bool correct, string? notes) ValidateOutput(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return (false, "output.txt is empty");

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numbers = new List<int>();

        foreach (var line in lines)
        {
            if (int.TryParse(line.Trim(), out var n))
                numbers.Add(n);
        }

        if (numbers.Count == 0)
            return (false, "No numbers found in output");

        var missing = ExpectedPrimes.Except(numbers).ToList();
        var extra = numbers.Except(ExpectedPrimes).ToList();

        if (missing.Count == 0 && extra.Count == 0)
            return (true, $"All {ExpectedPrimes.Length} primes correct");

        var notes = new List<string>();
        if (missing.Count > 0)
            notes.Add($"Missing: {string.Join(", ", missing)}");
        if (extra.Count > 0)
            notes.Add($"Extra: {string.Join(", ", extra)}");

        return (false, string.Join("; ", notes));
    }

    internal static string SanitizeModelName(string model)
    {
        var sanitized = model.ToLowerInvariant();
        var result = new char[sanitized.Length];
        for (var i = 0; i < sanitized.Length; i++)
        {
            var c = sanitized[i];
            result[i] = char.IsLetterOrDigit(c) || c == '-' ? c : '_';
        }
        var s = new string(result);
        return s.Length > 50 ? s[..50] : s;
    }

    private static void RenderResultsTable(List<BenchmarkResult> results)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold cyan]Benchmark Results[/]"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .AddColumn("[bold]Model[/]")
            .AddColumn("[bold]Run[/]")
            .AddColumn("[bold]LLM Time[/]")
            .AddColumn("[bold]Python Time[/]")
            .AddColumn("[bold]Result[/]")
            .AddColumn("[bold]Notes[/]");

        foreach (var model in results)
        {
            for (var i = 0; i < model.Runs.Count; i++)
            {
                var r = model.Runs[i];
                var modelCol = i == 0 ? Markup.Escape(model.ModelName) : "";
                var llmTime = r.LlmTimeMs > 0 ? $"{r.LlmTimeMs / 1000.0:F1}s" : "-";
                var pyTime = r.PythonExecTimeMs > 0 ? $"{r.PythonExecTimeMs}ms" : "-";
                var output = r.OutputCorrect switch
                {
                    true => "[green]PASS[/]",
                    false => "[red]FAIL[/]",
                    null => "[grey]N/A[/]"
                };

                table.AddRow(modelCol, $"#{i + 1}", llmTime, pyTime, output, Markup.Escape(r.Notes ?? ""));
            }

            // Average row
            var completedRuns = model.Runs.Where(r => r.LlmTimeMs > 0).ToList();
            var passCount = model.Runs.Count(r => r.OutputCorrect == true);
            var avgLlm = completedRuns.Count > 0
                ? $"[bold]{completedRuns.Average(r => r.LlmTimeMs) / 1000.0:F1}s[/]"
                : "-";
            var avgPy = completedRuns.Count(r => r.PythonExecTimeMs > 0) > 0
                ? $"[bold]{completedRuns.Where(r => r.PythonExecTimeMs > 0).Average(r => r.PythonExecTimeMs):F0}ms[/]"
                : "-";
            var passRate = $"[bold]{passCount}/{model.Runs.Count}[/]";

            table.AddRow("", "[bold]Avg[/]", avgLlm, avgPy, passRate, "");
            table.AddEmptyRow();
        }

        AnsiConsole.Write(table);
    }
}
