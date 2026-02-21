using System.Text.Json;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Mcp;
using Xunit;

namespace OpenOrca.Core.Tests;

public class McpClientTests
{
    [Fact]
    public void FormatRequest_Initialize_HasCorrectStructure()
    {
        var json = McpClient.FormatRequest(1, "initialize", JsonSerializer.SerializeToElement(new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "openorca", version = "0.4.0" }
        }));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("initialize", root.GetProperty("method").GetString());
        Assert.True(root.TryGetProperty("params", out var paramsProp));
        Assert.Equal("2024-11-05", paramsProp.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public void FormatRequest_ToolsList_NoParams()
    {
        var json = McpClient.FormatRequest(2, "tools/list");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(2, root.GetProperty("id").GetInt32());
        Assert.Equal("tools/list", root.GetProperty("method").GetString());
    }

    [Fact]
    public void FormatRequest_ToolCall_HasNameAndArguments()
    {
        var callParams = JsonSerializer.SerializeToElement(new
        {
            name = "read_file",
            arguments = new { path = "/tmp/test.txt" }
        });

        var json = McpClient.FormatRequest(3, "tools/call", callParams);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("tools/call", root.GetProperty("method").GetString());
        var p = root.GetProperty("params");
        Assert.Equal("read_file", p.GetProperty("name").GetString());
    }

    [Fact]
    public void ParseResponse_Success_ReturnsResult()
    {
        var response = """{"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":"test","description":"A test tool"}]}}""";

        var result = McpClient.ParseResponse(response);

        Assert.NotNull(result);
        Assert.True(result.Value.TryGetProperty("tools", out var tools));
        Assert.Equal(1, tools.GetArrayLength());
    }

    [Fact]
    public void ParseResponse_Error_ThrowsWithMessage()
    {
        var response = """{"jsonrpc":"2.0","id":1,"error":{"code":-32600,"message":"Invalid request"}}""";

        var ex = Assert.Throws<InvalidOperationException>(() => McpClient.ParseResponse(response));
        Assert.Contains("Invalid request", ex.Message);
    }

    [Fact]
    public void ParseResponse_NoResult_ReturnsNull()
    {
        var response = """{"jsonrpc":"2.0","id":1}""";

        var result = McpClient.ParseResponse(response);
        Assert.Null(result);
    }

    [Fact]
    public void McpToolDefinition_Defaults()
    {
        var def = new McpToolDefinition();
        Assert.Equal("", def.Name);
        Assert.Equal("", def.Description);
    }

    [Fact]
    public void McpClient_IsConnected_FalseBeforeConnect()
    {
        var client = new McpClient();
        Assert.False(client.IsConnected);
        Assert.Equal("", client.ServerName);
    }

    [Fact]
    public void FormatRequest_IncrementingIds_AreUnique()
    {
        var r1 = McpClient.FormatRequest(1, "test");
        var r2 = McpClient.FormatRequest(2, "test");

        using var d1 = JsonDocument.Parse(r1);
        using var d2 = JsonDocument.Parse(r2);

        Assert.NotEqual(
            d1.RootElement.GetProperty("id").GetInt32(),
            d2.RootElement.GetProperty("id").GetInt32());
    }
}
