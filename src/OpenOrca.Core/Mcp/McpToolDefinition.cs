using System.Text.Json;

namespace OpenOrca.Core.Mcp;

public sealed class McpToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public JsonElement InputSchema { get; set; }
}
