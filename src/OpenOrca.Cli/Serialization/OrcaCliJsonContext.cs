using System.Text.Json.Serialization;

namespace OpenOrca.Cli.Serialization;

/// <summary>
/// JSON output for --prompt / --print single-shot mode.
/// </summary>
public sealed class SinglePromptResult
{
    [JsonPropertyName("response")]
    public string Response { get; init; } = "";

    [JsonPropertyName("tokens")]
    public int Tokens { get; init; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; init; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCallRecord>? ToolCalls { get; init; }

    [JsonPropertyName("files_modified")]
    public List<string>? FilesModified { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;
}

/// <summary>
/// Record of a single tool call for JSON output.
/// </summary>
public sealed class ToolCallRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; init; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; init; }
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
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SinglePromptResult))]
[JsonSerializable(typeof(ProbePayload))]
[JsonSerializable(typeof(ToolCallRecord))]
[JsonSerializable(typeof(List<ToolCallRecord>))]
public partial class OrcaCliJsonContext : JsonSerializerContext;
