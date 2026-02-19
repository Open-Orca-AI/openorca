namespace OpenOrca.Tools.Abstractions;

public sealed class ToolResult
{
    public required string Content { get; init; }
    public bool IsError { get; init; }
    public bool WasDenied { get; init; }

    public static ToolResult Success(string content) => new() { Content = content };
    public static ToolResult Error(string message) => new() { Content = message, IsError = true };
    public static ToolResult Denied(string reason = "Permission denied by user.") =>
        new() { Content = reason, IsError = true, WasDenied = true };
}
