using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Shell;

namespace OpenOrca.Tools.Registry;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IOrcaTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ToolRegistry> _logger;

    // Tools to skip during auto-discovery (comment out entries to re-enable)
    private static readonly HashSet<Type> _disabledTools =
    [
        // All tools enabled — bash is now available
    ];

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    public void DiscoverTools(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();

        var toolTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IOrcaTool).IsAssignableFrom(t));

        foreach (var type in toolTypes)
        {
            if (_disabledTools.Contains(type))
            {
                _logger.LogInformation("Skipping disabled tool: {Type}", type.Name);
                continue;
            }

            try
            {
                if (Activator.CreateInstance(type) is IOrcaTool tool)
                {
                    Register(tool);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to instantiate tool: {Type}", type.Name);
            }
        }

        _logger.LogInformation("Discovered {Count} tools", _tools.Count);
    }

    public void Register(IOrcaTool tool)
    {
        _tools[tool.Name] = tool;
        _logger.LogDebug("Registered tool: {Name} (risk: {Risk})", tool.Name, tool.RiskLevel);
    }

    public IOrcaTool? Resolve(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }

    public IReadOnlyCollection<IOrcaTool> GetAll() => _tools.Values;

    public IList<AITool> GenerateAITools()
    {
        var tools = new List<AITool>();

        foreach (var tool in _tools.Values)
        {
            tools.Add(new OrcaAIFunction(tool));
        }

        return tools;
    }
}

/// <summary>
/// Custom AIFunction that wraps an IOrcaTool with its schema and delegates execution.
/// The actual execution is handled by the agent loop, not by AIFunction.InvokeCoreAsync.
/// </summary>
internal sealed class OrcaAIFunction : AIFunction
{
    private readonly IOrcaTool _tool;

    public OrcaAIFunction(IOrcaTool tool)
    {
        _tool = tool;
    }

    public override string Name => _tool.Name;
    public override string Description => _tool.Description;
    public override JsonElement JsonSchema => _tool.ParameterSchema;

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // This is not called in our flow; the agent loop handles execution directly.
        return new ValueTask<object?>("Tool execution handled by agent loop.");
    }
}
