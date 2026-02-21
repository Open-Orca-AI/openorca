using System.Text.Json.Serialization;

namespace OpenOrca.Cli.Serialization;

/// <summary>
/// JSON output for --prompt single-shot mode.
/// </summary>
public sealed class SinglePromptResult
{
    [JsonPropertyName("response")]
    public string Response { get; init; } = "";

    [JsonPropertyName("tokens")]
    public int Tokens { get; init; }
}

/// <summary>
/// Message shape for the raw HTTP probe request to LM Studio.
/// </summary>
public sealed class ProbeMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}

/// <summary>
/// Payload shape for the raw HTTP probe request to LM Studio.
/// </summary>
public sealed class ProbePayload
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("messages")]
    public ProbeMessage[] Messages { get; init; } = [];

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
}

/// <summary>
/// Source-generated JSON context for Cli-specific types.
/// </summary>
[JsonSerializable(typeof(SinglePromptResult))]
[JsonSerializable(typeof(ProbePayload))]
public partial class OrcaCliJsonContext : JsonSerializerContext;
