using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenOrca.Cli.Serialization;
using OpenOrca.Core.Configuration;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Probes the LLM API with descending max_tokens values to find the maximum
/// allowed value. This prevents rejected requests on providers like Groq Cloud
/// where different models have different max_tokens limits.
/// </summary>
internal sealed class MaxTokensNegotiator
{
    private static readonly int[] FallbackValues = [32768, 16384, 8192, 4096];

    // Matches patterns like: "less than or equal to `8192`" or "less than or equal to 8192"
    private static readonly Regex LimitRegex = new(
        @"less than or equal to `?(\d+)`?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _httpClient;

    public MaxTokensNegotiator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Probe the API with descending max_tokens values until one is accepted.
    /// Returns the negotiated value, or null if negotiation fails or should be skipped.
    /// </summary>
    public async Task<int?> NegotiateAsync(OrcaConfig config, ILogger logger, CancellationToken ct)
    {
        // Skip if user has explicitly set MaxTokens in config
        if (config.LmStudio.MaxTokens is not null)
        {
            logger.LogDebug("MaxTokens already set to {Value} — skipping negotiation", config.LmStudio.MaxTokens);
            return null;
        }

        var baseUrl = config.LmStudio.BaseUrl.TrimEnd('/');
        var apiKey = config.LmStudio.ApiKey;
        var model = config.LmStudio.Model ?? "default";

        // Try the first value; if the error tells us the limit, use that directly
        var firstValue = FallbackValues[0];
        var (accepted, errorBody) = await ProbeAsync(baseUrl, apiKey, model, firstValue, ct);

        if (accepted)
        {
            logger.LogInformation("Max tokens negotiation: {Value} accepted", firstValue);
            return firstValue;
        }

        // Try to parse the limit from the error message
        if (errorBody is not null)
        {
            var parsed = ParseLimitFromError(errorBody);
            if (parsed is not null)
            {
                logger.LogInformation("Max tokens negotiation: parsed limit {Value} from error", parsed);
                return parsed;
            }
        }

        // Fall back through remaining values
        for (var i = 1; i < FallbackValues.Length; i++)
        {
            var value = FallbackValues[i];
            var (ok, _) = await ProbeAsync(baseUrl, apiKey, model, value, ct);
            if (ok)
            {
                logger.LogInformation("Max tokens negotiation: {Value} accepted on fallback", value);
                return value;
            }
        }

        logger.LogWarning("Max tokens negotiation failed — all probe values rejected");
        return null;
    }

    /// <summary>
    /// Send a minimal probe request to test if the given max_tokens value is accepted.
    /// Returns (accepted, errorBody).
    /// </summary>
    internal async Task<(bool Accepted, string? ErrorBody)> ProbeAsync(
        string baseUrl, string apiKey, string model, int maxTokens, CancellationToken ct)
    {
        try
        {
            var payload = new ProbePayload
            {
                Model = model,
                Messages = [new ProbeMessage { Role = "user", Content = "hi" }],
                MaxTokens = maxTokens,
                Temperature = 0f,
                Stream = false
            };

            var json = JsonSerializer.Serialize(payload, OrcaCliJsonContext.Default.ProbePayload);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
                return (true, null);

            var body = await response.Content.ReadAsStringAsync(ct);
            return (false, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, null);
        }
    }

    /// <summary>
    /// Try to extract a numeric limit from an API error message.
    /// Looks for patterns like "less than or equal to `8192`".
    /// </summary>
    internal static int? ParseLimitFromError(string errorBody)
    {
        // Try to get the error message from JSON first
        string messageToSearch = errorBody;
        try
        {
            using var doc = JsonDocument.Parse(errorBody);
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                if (errorProp.ValueKind == JsonValueKind.String)
                    messageToSearch = errorProp.GetString() ?? errorBody;
                else if (errorProp.TryGetProperty("message", out var msgProp))
                    messageToSearch = msgProp.GetString() ?? errorBody;
            }
        }
        catch (JsonException)
        {
            // Not JSON, search the raw body
        }

        var match = LimitRegex.Match(messageToSearch);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var limit))
            return limit;

        return null;
    }
}
