using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.Network;

public sealed class NetworkDiagnosticsTool : IOrcaTool
{
    public string Name => "network_diagnostics";
    public string Description => "Network diagnostics: ping a host, perform DNS lookup, or check HTTP connectivity. Useful for debugging connection issues.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["ping", "dns_lookup", "check_connection"],
                "description": "Action: 'ping' a host, 'dns_lookup' to resolve a hostname, 'check_connection' to verify HTTP reachability"
            },
            "target": {
                "type": "string",
                "description": "Hostname, domain, or URL to test"
            },
            "timeout_seconds": {
                "type": "integer",
                "description": "Timeout in seconds. Defaults to 5."
            }
        },
        "required": ["action", "target"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var action = args.GetProperty("action").GetString()!;
        var target = args.GetProperty("target").GetString()!;
        var timeout = args.TryGetProperty("timeout_seconds", out var t) ? t.GetInt32() : 5;
        if (timeout <= 0 || timeout > 30) timeout = 5;

        return action switch
        {
            "ping" => await PingAsync(target, timeout, ct),
            "dns_lookup" => await DnsLookupAsync(target, ct),
            "check_connection" => await CheckConnectionAsync(target, timeout, ct),
            _ => ToolResult.Error($"Unknown action: {action}. Use 'ping', 'dns_lookup', or 'check_connection'.")
        };
    }

    private static async Task<ToolResult> PingAsync(string host, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            // Strip protocol if user passed a URL
            if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
                host = uri.Host;

            using var ping = new Ping();
            var sb = new StringBuilder();
            sb.AppendLine($"Pinging {host}...");

            var successes = 0;
            var totalMs = 0L;

            for (var i = 0; i < 4; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var reply = await ping.SendPingAsync(host, timeoutSeconds * 1000);
                    if (reply.Status == IPStatus.Success)
                    {
                        sb.AppendLine($"  Reply from {reply.Address}: time={reply.RoundtripTime}ms TTL={reply.Options?.Ttl}");
                        successes++;
                        totalMs += reply.RoundtripTime;
                    }
                    else
                    {
                        sb.AppendLine($"  {reply.Status}");
                    }
                }
                catch (PingException ex)
                {
                    sb.AppendLine($"  Error: {ex.InnerException?.Message ?? ex.Message}");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Result: {successes}/4 packets received");
            if (successes > 0)
                sb.AppendLine($"Average: {totalMs / successes}ms");

            return ToolResult.Success(sb.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Error($"Ping failed: {ex.Message}");
        }
    }

    private static async Task<ToolResult> DnsLookupAsync(string host, CancellationToken ct)
    {
        try
        {
            if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
                host = uri.Host;

            ct.ThrowIfCancellationRequested();
            var entry = await Dns.GetHostEntryAsync(host, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"DNS lookup for {host}:");
            sb.AppendLine($"  Hostname: {entry.HostName}");

            if (entry.AddressList.Length > 0)
            {
                sb.AppendLine($"  Addresses:");
                foreach (var addr in entry.AddressList)
                    sb.AppendLine($"    {addr} ({addr.AddressFamily})");
            }

            if (entry.Aliases.Length > 0)
            {
                sb.AppendLine($"  Aliases:");
                foreach (var alias in entry.Aliases)
                    sb.AppendLine($"    {alias}");
            }

            return ToolResult.Success(sb.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Error($"DNS lookup failed for {host}: {ex.Message}");
        }
    }

    private static async Task<ToolResult> CheckConnectionAsync(string target, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            // Ensure it's a full URL
            if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                target = "https://" + target;
            }

            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
                return ToolResult.Error($"Invalid URL: {target}");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            await DomainRateLimiter.ThrottleAsync(target, cts.Token);

            var response = await HttpHelper.Client.GetAsync(uri, cts.Token);

            return ToolResult.Success(
                $"Connection check for {target}:\n" +
                $"  Status: {(int)response.StatusCode} {response.ReasonPhrase}\n" +
                $"  Server: {response.Headers.Server}\n" +
                $"  Content-Type: {response.Content.Headers.ContentType}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolResult.Error($"Connection check timed out for {target} (after {timeoutSeconds}s)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Error($"Connection check failed for {target}: {ex.Message}");
        }
    }
}
