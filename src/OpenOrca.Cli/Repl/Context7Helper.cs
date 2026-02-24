using System.Text.Json;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Registry;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Helper that detects and invokes Context7 MCP tools for fetching library documentation.
/// Used by system prompt injection, the /docs command, and @docs: preprocessing.
/// </summary>
internal sealed class Context7Helper
{
    private readonly ToolRegistry _toolRegistry;

    public Context7Helper(ToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    /// <summary>
    /// Returns true if both Context7 tools (resolve-library-id and get-library-docs/query-docs) are available.
    /// </summary>
    public bool IsAvailable()
    {
        var (resolve, query) = FindTools();
        return resolve is not null && query is not null;
    }

    /// <summary>
    /// Resolve a library name to a Context7-compatible library ID.
    /// Returns the library ID string, or null if resolution fails.
    /// </summary>
    public async Task<string?> ResolveLibraryIdAsync(string libraryName, CancellationToken ct = default)
    {
        var (resolveTool, _) = FindTools();
        if (resolveTool is null)
            return null;

        var args = BuildArgs(("libraryName", libraryName));
        var result = await resolveTool.ExecuteAsync(args, ct);

        if (result.IsError || string.IsNullOrWhiteSpace(result.Content))
            return null;

        return result.Content.Trim();
    }

    /// <summary>
    /// Query documentation for a resolved library ID.
    /// Returns the documentation text, or null on failure.
    /// </summary>
    public async Task<string?> QueryDocsAsync(string libraryId, string? query = null, CancellationToken ct = default)
    {
        var (_, queryTool) = FindTools();
        if (queryTool is null)
            return null;

        var args = query is not null
            ? BuildArgs(("libraryId", libraryId), ("query", query))
            : BuildArgs(("libraryId", libraryId));
        var result = await queryTool.ExecuteAsync(args, ct);

        if (result.IsError || string.IsNullOrWhiteSpace(result.Content))
            return null;

        return result.Content;
    }

    /// <summary>
    /// Combined convenience: resolve a library name then query its docs.
    /// Returns the documentation text, or null if either step fails.
    /// </summary>
    public async Task<string?> FetchDocsAsync(string libraryName, string? query = null, CancellationToken ct = default)
    {
        var libraryId = await ResolveLibraryIdAsync(libraryName, ct);
        if (libraryId is null)
            return null;

        return await QueryDocsAsync(libraryId, query, ct);
    }

    private static JsonElement BuildArgs(params (string key, string value)[] pairs)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in pairs)
                writer.WriteString(key, value);
            writer.WriteEndObject();
        }
        return JsonDocument.Parse(stream.ToArray()).RootElement;
    }

    private (IOrcaTool? resolve, IOrcaTool? query) FindTools()
    {
        IOrcaTool? resolve = null;
        IOrcaTool? query = null;

        foreach (var tool in _toolRegistry.GetAll())
        {
            var name = tool.Name;
            if (name.EndsWith("resolve-library-id", StringComparison.OrdinalIgnoreCase))
                resolve = tool;
            else if (name.EndsWith("get-library-docs", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith("query-docs", StringComparison.OrdinalIgnoreCase))
                query = tool;
        }

        return (resolve, query);
    }
}
