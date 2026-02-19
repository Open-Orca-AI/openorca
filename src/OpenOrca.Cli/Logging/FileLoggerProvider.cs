using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OpenOrca.Cli.Logging;

/// <summary>
/// Lightweight file logger. Writes to ~/.openorca/logs/openorca-{date}.log.
/// One file per day, appends on each run.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly LogLevel _minLevel;
    private StreamWriter? _writer;
    private readonly object _lock = new();
    private string? _currentDate;

    public FileLoggerProvider(LogLevel minLevel = LogLevel.Debug)
    {
        _minLevel = minLevel;
        var configDir = Core.Configuration.ConfigManager.GetConfigDirectory();
        _logDirectory = Path.Combine(configDir, "logs");
        Directory.CreateDirectory(_logDirectory);
        EnsureWriter();
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this, _minLevel));

    internal void WriteEntry(string category, LogLevel level, string message)
    {
        lock (_lock)
        {
            EnsureWriter();
            _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{LevelTag(level)}] {category}: {message}");
            _writer?.Flush();
        }
    }

    private void EnsureWriter()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (_currentDate == today && _writer is not null) return;

        _writer?.Dispose();
        _currentDate = today;
        var path = Path.Combine(_logDirectory, $"openorca-{today}.log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = false };

        _writer.WriteLine($"--- OpenOrca log opened at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
        _writer.Flush();
    }

    private static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly FileLoggerProvider _provider;
    private readonly LogLevel _minLevel;

    public FileLogger(string category, FileLoggerProvider provider, LogLevel minLevel)
    {
        // Shorten "OpenOrca.Cli.Repl.ReplLoop" → "ReplLoop"
        _category = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
        _provider = provider;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (exception is not null)
            message += $"\n  Exception: {exception.GetType().Name}: {exception.Message}\n  {exception.StackTrace}";

        _provider.WriteEntry(_category, logLevel, message);
    }
}
