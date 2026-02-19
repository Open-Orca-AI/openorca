using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenOrca.Core.Configuration;

namespace OpenOrca.Core.Client;

public sealed class ModelDiscovery
{
    private readonly OrcaConfig _config;
    private readonly ILogger<ModelDiscovery> _logger;

    public ModelDiscovery(OrcaConfig config, ILogger<ModelDiscovery> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        var baseUrl = _config.LmStudio.BaseUrl.TrimEnd('/');
        // Go up from /v1 to get the models endpoint
        var modelsUrl = baseUrl.EndsWith("/v1")
            ? $"{baseUrl}/models"
            : $"{baseUrl}/v1/models";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.LmStudio.ApiKey}");

        try
        {
            var response = await http.GetFromJsonAsync<JsonElement>(modelsUrl, ct);

            if (response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                var models = new List<string>();
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id))
                        models.Add(id.GetString()!);
                }
                _logger.LogInformation("Found {Count} models at {Url}", models.Count, modelsUrl);
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
