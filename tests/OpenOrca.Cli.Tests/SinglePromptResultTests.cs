using System.Text.Json;
using OpenOrca.Cli.Serialization;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class SinglePromptResultTests
{
    [Fact]
    public void SinglePromptResult_Serializes_BasicFields()
    {
        var result = new SinglePromptResult
        {
            Response = "Hello",
            Tokens = 42,
            DurationMs = 1234,
            Success = true
        };

        var json = JsonSerializer.Serialize(result, OrcaCliJsonContext.Default.SinglePromptResult);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Hello", root.GetProperty("response").GetString());
        Assert.Equal(42, root.GetProperty("tokens").GetInt32());
        Assert.Equal(1234, root.GetProperty("duration_ms").GetInt64());
        Assert.True(root.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void SinglePromptResult_Serializes_ToolCalls()
    {
        var result = new SinglePromptResult
        {
            Response = "Done",
            Tokens = 10,
            DurationMs = 500,
            ToolCalls =
            [
                new ToolCallRecord
                {
                    Name = "read_file",
                    Arguments = "{\"path\":\"test.txt\"}",
                    Result = "file contents",
                    IsError = false,
                    DurationMs = 100
                }
            ],
            Success = true
        };

        var json = JsonSerializer.Serialize(result, OrcaCliJsonContext.Default.SinglePromptResult);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("tool_calls", out var tcArr));
        Assert.Equal(JsonValueKind.Array, tcArr.ValueKind);
        Assert.Equal(1, tcArr.GetArrayLength());

        var tc = tcArr[0];
        Assert.Equal("read_file", tc.GetProperty("name").GetString());
        Assert.Equal("{\"path\":\"test.txt\"}", tc.GetProperty("arguments").GetString());
        Assert.False(tc.GetProperty("is_error").GetBoolean());
    }

    [Fact]
    public void SinglePromptResult_Serializes_FilesModified()
    {
        var result = new SinglePromptResult
        {
            Response = "Created",
            Tokens = 5,
            DurationMs = 300,
            FilesModified = ["src/main.cs", "test/test.cs"],
            Success = true
        };

        var json = JsonSerializer.Serialize(result, OrcaCliJsonContext.Default.SinglePromptResult);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("files_modified", out var files));
        Assert.Equal(2, files.GetArrayLength());
        Assert.Equal("src/main.cs", files[0].GetString());
    }

    [Fact]
    public void SinglePromptResult_NullToolCalls_OmittedInJson()
    {
        var result = new SinglePromptResult
        {
            Response = "Hi",
            Tokens = 1,
            Success = true
        };

        var json = JsonSerializer.Serialize(result, OrcaCliJsonContext.Default.SinglePromptResult);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Null lists should not appear in the JSON output
        Assert.False(root.TryGetProperty("tool_calls", out _));
        Assert.False(root.TryGetProperty("files_modified", out _));
    }

    [Fact]
    public void SinglePromptResult_SuccessDefaultsTrue()
    {
        var result = new SinglePromptResult();
        Assert.True(result.Success);
    }

    [Fact]
    public void ToolCallRecord_Serializes_ErrorCall()
    {
        var record = new ToolCallRecord
        {
            Name = "bash",
            Arguments = "{\"command\":\"ls\"}",
            Result = "Permission denied",
            IsError = true,
            DurationMs = 50
        };

        var json = JsonSerializer.Serialize(record, OrcaCliJsonContext.Default.ToolCallRecord);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("bash", root.GetProperty("name").GetString());
        Assert.True(root.GetProperty("is_error").GetBoolean());
        Assert.Equal(50, root.GetProperty("duration_ms").GetInt64());
    }

    [Fact]
    public void SinglePromptResult_Roundtrips_ViaJson()
    {
        var original = new SinglePromptResult
        {
            Response = "Test response",
            Tokens = 100,
            DurationMs = 2000,
            ToolCalls =
            [
                new ToolCallRecord { Name = "tool1", IsError = false, DurationMs = 500 },
                new ToolCallRecord { Name = "tool2", IsError = true, Result = "error", DurationMs = 100 }
            ],
            FilesModified = ["file1.cs"],
            Success = false
        };

        var json = JsonSerializer.Serialize(original, OrcaCliJsonContext.Default.SinglePromptResult);
        var deserialized = JsonSerializer.Deserialize(json, OrcaCliJsonContext.Default.SinglePromptResult);

        Assert.NotNull(deserialized);
        Assert.Equal("Test response", deserialized.Response);
        Assert.Equal(100, deserialized.Tokens);
        Assert.Equal(2000, deserialized.DurationMs);
        Assert.False(deserialized.Success);
        Assert.NotNull(deserialized.ToolCalls);
        Assert.Equal(2, deserialized.ToolCalls.Count);
        Assert.NotNull(deserialized.FilesModified);
        Assert.Single(deserialized.FilesModified);
    }
}
