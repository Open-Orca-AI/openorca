using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.UtilityTools;

public sealed class TaskListTool : IOrcaTool
{
    public string Name => "task_list";
    public string Description => "Manage a session-scoped task list to track progress on complex work. Actions: add (create task), list (show all), complete (mark done), remove (delete). Helps organize multi-step tasks and show progress.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["add", "list", "complete", "remove"],
                "description": "Task action. Defaults to 'list'."
            },
            "task": {
                "type": "string",
                "description": "Task description (required for 'add')"
            },
            "id": {
                "type": "integer",
                "description": "Task ID (required for 'complete' and 'remove')"
            }
        }
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var action = args.TryGetProperty("action", out var a) ? a.GetString() ?? "list" : "list";

        return action switch
        {
            "add" => HandleAdd(args),
            "complete" => HandleComplete(args),
            "remove" => HandleRemove(args),
            _ => Task.FromResult(ToolResult.Success(TaskStore.List()))
        };
    }

    private static Task<ToolResult> HandleAdd(JsonElement args)
    {
        if (!args.TryGetProperty("task", out var t) || string.IsNullOrWhiteSpace(t.GetString()))
            return Task.FromResult(ToolResult.Error("Task description is required for 'add'."));

        var id = TaskStore.Add(t.GetString()!);
        return Task.FromResult(ToolResult.Success($"Added task #{id}: {t.GetString()}\n\n{TaskStore.List()}"));
    }

    private static Task<ToolResult> HandleComplete(JsonElement args)
    {
        if (!args.TryGetProperty("id", out var idProp))
            return Task.FromResult(ToolResult.Error("Task ID is required for 'complete'."));

        var id = idProp.GetInt32();
        if (!TaskStore.Complete(id))
            return Task.FromResult(ToolResult.Error($"Task #{id} not found."));

        return Task.FromResult(ToolResult.Success($"Completed task #{id}.\n\n{TaskStore.List()}"));
    }

    private static Task<ToolResult> HandleRemove(JsonElement args)
    {
        if (!args.TryGetProperty("id", out var idProp))
            return Task.FromResult(ToolResult.Error("Task ID is required for 'remove'."));

        var id = idProp.GetInt32();
        if (!TaskStore.Remove(id))
            return Task.FromResult(ToolResult.Error($"Task #{id} not found."));

        return Task.FromResult(ToolResult.Success($"Removed task #{id}.\n\n{TaskStore.List()}"));
    }
}
