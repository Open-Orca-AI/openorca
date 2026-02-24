namespace OpenOrca.Tools.Utilities;

/// <summary>
/// Retries file operations that fail with IOException (e.g., "file is being used by another process").
/// Uses exponential backoff: 50ms, 100ms, 200ms (3 retries by default).
/// </summary>
public static class FileRetryHelper
{
    private const int DefaultMaxRetries = 3;
    private const int BaseDelayMs = 50;

    /// <summary>
    /// Execute an async file operation with retry on IOException.
    /// </summary>
    public static async Task RetryOnIOExceptionAsync(Func<Task> operation, CancellationToken ct, int maxRetries = DefaultMaxRetries)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (IOException) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                await Task.Delay(BaseDelayMs * (1 << attempt), ct);
            }
        }
    }

    /// <summary>
    /// Execute a synchronous file operation with retry on IOException.
    /// </summary>
    public static async Task RetryOnIOExceptionAsync(Action operation, CancellationToken ct, int maxRetries = DefaultMaxRetries)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (IOException) when (attempt < maxRetries && !ct.IsCancellationRequested)
            {
                await Task.Delay(BaseDelayMs * (1 << attempt), ct);
            }
        }
    }
}
