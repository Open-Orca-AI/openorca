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
            Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process.");

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
            try { process.Kill(entireProcessTree: true); } catch { }
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
    private readonly object _lock = new();
    private readonly Process _process;

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

    public int TotalLinesCaptured
    {
        get { lock (_lock) { return _outputLines.Count; } }
    }

    internal void Kill()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    public void Dispose()
    {
        Kill();
        _process.Dispose();
    }
}
