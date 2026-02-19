using System.Text;
using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Utility;

public sealed class EnvTool : IOrcaTool
{
    public string Name => "env";
    public string Description => "Inspect environment variables. Use action 'get' to get a specific variable, 'list' to list all (or filter by prefix).";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["get", "list"],
                "description": "Action to perform: 'get' a specific variable or 'list' all variables"
            },
            "name": {
                "type": "string",
                "description": "Variable name (required for 'get')"
            },
            "prefix": {
                "type": "string",
                "description": "Filter variables by prefix (optional for 'list')"
            }
        },
        "required": ["action"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var action = args.GetProperty("action").GetString()!;

        return action switch
        {
            "get" => Task.FromResult(GetVariable(args)),
            "list" => Task.FromResult(ListVariables(args)),
            _ => Task.FromResult(ToolResult.Error($"Unknown action: {action}. Use 'get' or 'list'."))
        };
    }

    private static ToolResult GetVariable(JsonElement args)
    {
        if (!args.TryGetProperty("name", out var nameProp))
            return ToolResult.Error("'name' is required for action 'get'.");

        var name = nameProp.GetString()!;
        var value = Environment.GetEnvironmentVariable(name);

        return value is not null
            ? ToolResult.Success($"{name}={value}")
            : ToolResult.Error($"Environment variable '{name}' is not set.");
    }

    private static ToolResult ListVariables(JsonElement args)
    {
        var prefix = args.TryGetProperty("prefix", out var p) ? p.GetString() : null;
        var vars = Environment.GetEnvironmentVariables();

        var sb = new StringBuilder();
        var count = 0;

        foreach (string key in vars.Keys)
        {
            if (prefix is not null && !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            sb.AppendLine($"{key}={vars[key]}");
            count++;

            if (count >= 200)
            {
                sb.AppendLine($"... (truncated at 200 entries)");
                break;
            }
        }

        return count > 0
            ? ToolResult.Success($"Found {count} variables:\n{sb}")
            : ToolResult.Success("No environment variables found matching the criteria.");
    }
}
