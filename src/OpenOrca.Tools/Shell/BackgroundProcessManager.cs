using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpenOrca.Tools.Shell;

public static class BackgroundProcessManager
{
    private static readonly ConcurrentDictionary<string, ManagedProcess> _processes = new();

    static BackgroundProcessManager()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
    }

    public static void KillAll()
    {
        foreach (var mp in _processes.Values)
        {
            mp.Kill();
        }
    }

    public static ManagedProcess Start(string command, string workingDirectory)
    {
        var id = Guid.NewGuid().ToString("N")[..4];

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/bash",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process.");

        // Write command via stdin to avoid shell argument escaping issues
        if (isWindows)
            process.StandardInput.WriteLine("@echo off");
        process.StandardInput.WriteLine(command);
        process.StandardInput.Close();

        try
        {
            var managed = new ManagedProcess(id, command, workingDirectory, process);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    managed.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    managed.AppendLine($"[stderr] {e.Data}");
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _processes[id] = managed;
            return managed;
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.Dispose();
            throw;
        }
    }

    public static ManagedProcess? Get(string id)
    {
        _processes.TryGetValue(id, out var mp);
        return mp;
    }

    public static IReadOnlyList<ManagedProcess> ListAll() => _processes.Values.ToList();

    public static bool Stop(string id)
    {
        if (!_processes.TryGetValue(id, out var mp))
            return false;

        mp.Kill();
        return true;
    }
}

public sealed class ManagedProcess : IDisposable
{
    private const int MaxLines = 1000;
    private readonly List<string> _outputLines = new();
    private readonly Lock _lock = new();
    private readonly Process _process;
    private int _totalAppended;

    public string Id { get; }
    public string Command { get; }
    public string WorkingDirectory { get; }
    public DateTime StartTimeUtc { get; }

    public bool HasExited => _process.HasExited;
    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    internal ManagedProcess(string id, string command, string workingDirectory, Process process)
    {
        Id = id;
        Command = command;
        WorkingDirectory = workingDirectory;
        StartTimeUtc = DateTime.UtcNow;
        _process = process;
    }

    internal void AppendLine(string line)
    {
        lock (_lock)
        {
            _outputLines.Add(line);
            _totalAppended++;
            if (_outputLines.Count > MaxLines)
                _outputLines.RemoveAt(0);
        }
    }

    public List<string> GetTailLines(int count)
    {
        lock (_lock)
        {
            var start = Math.Max(0, _outputLines.Count - count);
            return _outputLines.GetRange(start, _outputLines.Count - start);
        }
    }

    /// <summary>
    /// Returns only lines added since the cursor position, and the updated cursor.
    /// Handles ring buffer eviction correctly.
    /// </summary>
    public (List<string> Lines, int NewCursor) GetNewLines(int cursor)
    {
        lock (_lock)
        {
            if (cursor >= _totalAppended)
                return (new List<string>(), cursor);

            var newCount = _totalAppended - cursor;
            var available = Math.Min(newCount, _outputLines.Count);
            var start = _outputLines.Count - available;
            return (_outputLines.GetRange(start, available), _totalAppended);
        }
    }

    /// <summary>
    /// Wait for the process to exit, with a timeout. Returns true if exited, false if timed out.
    /// </summary>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public int TotalLinesCaptured
    {
        get { lock (_lock) { return _totalAppended; } }
    }

    internal void Kill()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process already exited between check and kill — safe to ignore
        }
    }

    public void Dispose()
    {
        Kill();
        _process.Dispose();
    }
}
