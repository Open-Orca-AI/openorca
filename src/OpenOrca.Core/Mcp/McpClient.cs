using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenOrca.Core.Configuration;

namespace OpenOrca.Core.Mcp;

/// <summary>
/// MCP JSON-RPC 2.0 client over stdio transport.
/// Communicates with MCP servers via stdin/stdout using newline-delimited JSON.
/// </summary>
public sealed class McpClient : IAsyncDisposable
{
    private Process? _process;
    private int _requestId;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string ServerName { get; private set; } = "";
    public bool IsConnected => _process is not null && !_process.HasExited;

    public McpClient(ILogger? logger = null)
    {
        _logger = logger;
    }

    public async Task ConnectAsync(string serverName, McpServerConfig config, CancellationToken ct)
    {
        ServerName = serverName;

        var psi = new ProcessStartInfo(config.Command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };

        foreach (var arg in config.Args)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in config.Env)
            psi.Environment[key] = value;

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start MCP server: {config.Command}");

        _logger?.LogInformation("MCP server '{Name}' started (PID: {Pid})", serverName, _process.Id);

        // Send initialize
        var initResult = await SendRequestAsync("initialize", JsonSerializer.SerializeToElement(new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "openorca", version = "0.7.0" }
        }), ct);

        _logger?.LogDebug("MCP initialize response: {Result}", initResult.GetRawText());

        // Send initialized notification (no id, no response expected)
        await SendNotificationAsync("notifications/initialized", default, ct);
    }

    public async Task<List<McpToolDefinition>> ListToolsAsync(CancellationToken ct)
    {
        var result = await SendRequestAsync("tools/list", default, ct);
        var tools = new List<McpToolDefinition>();

        if (result.TryGetProperty("tools", out var toolsArr) && toolsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolEl in toolsArr.EnumerateArray())
            {
                var def = new McpToolDefinition
                {
                    Name = toolEl.GetProperty("name").GetString() ?? "",
                    Description = toolEl.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                    InputSchema = toolEl.TryGetProperty("inputSchema", out var schema) ? schema.Clone() : default
                };
                tools.Add(def);
            }
        }

        _logger?.LogInformation("MCP server '{Name}' has {Count} tools", ServerName, tools.Count);
        return tools;
    }

    public async Task<string> CallToolAsync(string name, JsonElement args, CancellationToken ct)
    {
        var paramsEl = JsonSerializer.SerializeToElement(new
        {
            name,
            arguments = args
        });

        var result = await SendRequestAsync("tools/call", paramsEl, ct);

        // Extract text content from MCP tool result
        if (result.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in contentArr.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var text))
                    sb.AppendLine(text.GetString());
            }
            return sb.ToString().TrimEnd();
        }

        return result.GetRawText();
    }

    internal async Task<JsonElement> SendRequestAsync(string method, JsonElement? parameters, CancellationToken ct)
    {
        if (_process is null || _process.HasExited)
            throw new InvalidOperationException($"MCP server '{ServerName}' is not running");

        var id = Interlocked.Increment(ref _requestId);

        var request = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method
        };

        if (parameters.HasValue && parameters.Value.ValueKind != JsonValueKind.Undefined)
            request["params"] = parameters.Value;

        var json = JsonSerializer.Serialize(request);
        _logger?.LogDebug("MCP → {Server}: {Json}", ServerName, json.Length > 500 ? json[..500] + "..." : json);

        await _writeLock.WaitAsync(ct);
        try
        {
            await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }

        // Read response lines until we get one with matching id
        while (!ct.IsCancellationRequested)
        {
            var line = await _process.StandardOutput.ReadLineAsync(ct);
            if (line is null)
                throw new InvalidOperationException($"MCP server '{ServerName}' closed stdout unexpectedly");

            if (string.IsNullOrWhiteSpace(line))
                continue;

            _logger?.LogDebug("MCP ← {Server}: {Line}", ServerName, line.Length > 500 ? line[..500] + "..." : line);

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // Skip notifications (no id field)
                if (!root.TryGetProperty("id", out var idProp))
                    continue;

                if (idProp.GetInt32() != id)
                    continue;

                if (root.TryGetProperty("error", out var error))
                {
                    var errorMsg = error.TryGetProperty("message", out var msg) ? msg.GetString() : error.GetRawText();
                    throw new InvalidOperationException($"MCP error from '{ServerName}': {errorMsg}");
                }

                if (root.TryGetProperty("result", out var resultEl))
                    return resultEl.Clone();

                return default;
            }
            catch (JsonException)
            {
                _logger?.LogWarning("MCP: non-JSON line from {Server}: {Line}", ServerName, line.Length > 200 ? line[..200] : line);
            }
        }

        throw new OperationCanceledException();
    }

    internal async Task SendNotificationAsync(string method, JsonElement? parameters, CancellationToken ct)
    {
        if (_process is null || _process.HasExited) return;

        var notification = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        if (parameters.HasValue && parameters.Value.ValueKind != JsonValueKind.Undefined)
            notification["params"] = parameters.Value;

        var json = JsonSerializer.Serialize(notification);

        await _writeLock.WaitAsync(ct);
        try
        {
            await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Format a JSON-RPC request message (for testing).
    /// </summary>
    public static string FormatRequest(int id, string method, JsonElement? parameters = null)
    {
        var request = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method
        };

        if (parameters.HasValue && parameters.Value.ValueKind != JsonValueKind.Undefined)
            request["params"] = parameters.Value;

        return JsonSerializer.Serialize(request);
    }

    /// <summary>
    /// Parse a JSON-RPC response and extract the result (for testing).
    /// </summary>
    public static JsonElement? ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            var errorMsg = error.TryGetProperty("message", out var msg) ? msg.GetString() : error.GetRawText();
            throw new InvalidOperationException($"MCP error: {errorMsg}");
        }

        if (root.TryGetProperty("result", out var result))
            return result.Clone();

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing MCP server '{Name}'", ServerName);
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        _writeLock.Dispose();
    }
}
