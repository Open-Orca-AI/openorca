using System.Text.Json;

namespace OpenOrca.Tools.Abstractions;

/// <summary>
/// Optional interface for tools that support streaming partial output to the user
/// in real-time during execution. The callback is for display; the returned ToolResult
/// is the complete result for the LLM conversation.
/// </summary>
public interface IStreamingOrcaTool : IOrcaTool
{
    /// <summary>
    /// Execute the tool, streaming partial output via the callback as it becomes available.
    /// The callback is called for each line/chunk of output (for real-time user display).
    /// Returns the final ToolResult when complete (for the LLM conversation).
    /// </summary>
    Task<ToolResult> ExecuteStreamingAsync(
        JsonElement args, Action<string> onOutput, CancellationToken ct = default);
}
