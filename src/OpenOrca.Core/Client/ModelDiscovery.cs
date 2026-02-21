using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Serialization;

namespace OpenOrca.Core.Client;

public sealed class ModelDiscovery
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static List<string>? _cachedModels;
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly OrcaConfig _config;
    private readonly ILogger<ModelDiscovery> _logger;

    public ModelDiscovery(OrcaConfig config, ILogger<ModelDiscovery> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        if (_cachedModels is not null && DateTime.UtcNow < _cacheExpiry)
            return _cachedModels;

        var baseUrl = _config.LmStudio.BaseUrl.TrimEnd('/');
        // Go up from /v1 to get the models endpoint
        var modelsUrl = baseUrl.EndsWith("/v1")
            ? $"{baseUrl}/models"
            : $"{baseUrl}/v1/models";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            request.Headers.Add("Authorization", $"Bearer {_config.LmStudio.ApiKey}");
            using var httpResponse = await SharedHttpClient.SendAsync(request, ct);
            httpResponse.EnsureSuccessStatusCode();
            var response = await httpResponse.Content.ReadFromJsonAsync(OrcaJsonContext.Default.JsonElement, ct);

            if (response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                var models = new List<string>();
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id))
                        models.Add(id.GetString()!);
                }
                _logger.LogInformation("Found {Count} models at {Url}", models.Count, modelsUrl);
                _cachedModels = models;
                _cacheExpiry = DateTime.UtcNow + CacheTtl;
                return models;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover models from {Url}", modelsUrl);
        }

        return [];
    }

    public async Task<bool> CheckConnectivityAsync(CancellationToken ct = default)
    {
        var models = await GetAvailableModelsAsync(ct);
        return models.Count > 0;
    }
}
