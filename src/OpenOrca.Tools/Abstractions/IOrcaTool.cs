using System.Text.Json;

namespace OpenOrca.Tools.Abstractions;

public interface IOrcaTool
{
    string Name { get; }
    string Description { get; }
    ToolRiskLevel RiskLevel { get; }

    /// <summary>
    /// JSON Schema describing the tool's parameters for OpenAI function calling.
    /// </summary>
    JsonElement ParameterSchema { get; }

    Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default);
}
