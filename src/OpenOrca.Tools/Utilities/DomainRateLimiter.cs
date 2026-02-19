using System.Collections.Concurrent;

namespace OpenOrca.Tools.Utilities;

/// <summary>
/// Per-domain rate limiter that enforces a minimum delay between requests
/// to the same host. Thread-safe for concurrent tool execution.
/// </summary>
public static class DomainRateLimiter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Semaphores = new();
    private static readonly ConcurrentDictionary<string, DateTime> LastRequestTime = new();

    /// <summary>
    /// Minimum delay between requests to the same domain.
    /// </summary>
    public static TimeSpan MinDelay { get; set; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Wait until the rate limit window has passed for the given domain.
    /// </summary>
    public static async Task ThrottleAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        var domain = uri.Host.ToLowerInvariant();
        var semaphore = Semaphores.GetOrAdd(domain, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            if (LastRequestTime.TryGetValue(domain, out var lastTime))
            {
                var elapsed = DateTime.UtcNow - lastTime;
                if (elapsed < MinDelay)
                    await Task.Delay(MinDelay - elapsed, ct);
            }

            LastRequestTime[domain] = DateTime.UtcNow;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
