using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenOrca.Core.Configuration;

namespace OpenOrca.Core.Client;

public sealed class LmStudioClientFactory
{
    private readonly OrcaConfig _config;
    private readonly ILogger<LmStudioClientFactory> _logger;

    public LmStudioClientFactory(OrcaConfig config, ILogger<LmStudioClientFactory> logger)
    {
        _config = config;
        _logger = logger;
    }

    public IChatClient Create(string? modelOverride = null)
    {
        var lmConfig = _config.LmStudio;
        var model = modelOverride ?? lmConfig.Model ?? "default";

        _logger.LogInformation("Creating LM Studio client: {BaseUrl}, model: {Model}", lmConfig.BaseUrl, model);

        var credential = new ApiKeyCredential(lmConfig.ApiKey);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(lmConfig.BaseUrl)
        };

        var openAiClient = new OpenAIClient(credential, options);
        return openAiClient.GetChatClient(model).AsIChatClient();
    }
}
