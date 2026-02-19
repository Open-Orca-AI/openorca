using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Cli.Repl;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class ToolCallParserTests
{
    private readonly ToolCallParser _parser = new(NullLogger.Instance);

    // ── Pattern 1: <tool_call> tags ──

    [Fact]
    public void Parse_ToolCallTags_ExtractsNameAndArguments()
    {
        var text = """
            I'll read the file for you.
            <tool_call>
            {"name": "read_file", "arguments": {"path": "/tmp/test.txt"}}
            </tool_call>
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("read_file", results[0].Name);
        Assert.NotNull(results[0].Arguments);
        Assert.Equal("/tmp/test.txt", results[0].Arguments!["path"]?.ToString());
    }

    [Fact]
    public void Parse_MultipleToolCallTags_ExtractsAll()
    {
        var text = """
            <tool_call>{"name": "read_file", "arguments": {"path": "a.txt"}}</tool_call>
            <tool_call>{"name": "write_file", "arguments": {"path": "b.txt", "content": "hello"}}</tool_call>
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Equal(2, results.Count);
        Assert.Equal("read_file", results[0].Name);
        Assert.Equal("write_file", results[1].Name);
    }

    // ── Pattern 2: <|tool_call|> pipe-delimited tags ──

    [Fact]
    public void Parse_PipeDelimitedTags_ExtractsToolCall()
    {
        var text = """
            <|tool_call|>
            {"name": "bash", "arguments": {"command": "ls -la"}}
            <|/tool_call|>
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("bash", results[0].Name);
    }

    // ── Pattern 3: [TOOL_CALL] bracket tags ──

    [Fact]
    public void Parse_BracketTags_ExtractsToolCall()
    {
        var text = """
            [TOOL_CALL]
            {"name": "grep", "arguments": {"pattern": "TODO", "path": "."}}
            [/TOOL_CALL]
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("grep", results[0].Name);
    }

    // ── Pattern 4: JSON in code fences ──

    [Fact]
    public void Parse_JsonCodeFence_ExtractsToolCall()
    {
        var text = """
            Let me search for that:
            ```json
            {"name": "grep", "arguments": {"pattern": "error", "path": "/var/log"}}
            ```
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("grep", results[0].Name);
    }

    [Fact]
    public void Parse_CodeFenceWithoutJsonLabel_ExtractsToolCall()
    {
        var text = """
            ```
            {"name": "list_files", "arguments": {"path": "."}}
            ```
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("list_files", results[0].Name);
    }

    // ── Pattern 4b: Unclosed <tool_call> tag ──

    [Fact]
    public void Parse_UnclosedToolCallTag_ExtractsPartialToolCall()
    {
        var text = """
            <tool_call>
            {"name": "read_file", "arguments": {"path": "test.cs"}}
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("read_file", results[0].Name);
    }

    // ── Pattern 5: Bare JSON ──

    [Fact]
    public void Parse_BareJsonWithNameAndArguments_ExtractsToolCall()
    {
        var text = """{"name": "bash", "arguments": {"command": "echo hello"}}""";

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("bash", results[0].Name);
    }

    // ── <think> tag handling ──

    [Fact]
    public void Parse_ThinkTags_StrippedBeforeParsing()
    {
        var text = """
            <think>Let me think about which tool to use... I'll use read_file.</think>
            <tool_call>{"name": "read_file", "arguments": {"path": "config.json"}}</tool_call>
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("read_file", results[0].Name);
    }

    [Fact]
    public void Parse_UnclosedThinkTag_StrippedBeforeParsing()
    {
        var text = """
            <think>Still thinking about this...
            <tool_call>{"name": "bash", "arguments": {"command": "pwd"}}</tool_call>
            """;

        // The tool call is inside the unclosed think — parser should still find it via fallback
        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("bash", results[0].Name);
    }

    // ── <assistant> tag stripping ──

    [Fact]
    public void Parse_AssistantTags_StrippedBeforeParsing()
    {
        var text = """
            <assistant>Some intro text</assistant>
            <tool_call>{"name": "read_file", "arguments": {"path": "test.txt"}}</tool_call>
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("read_file", results[0].Name);
    }

    // ── "parameters" alias ──

    [Fact]
    public void Parse_ParametersAlias_TreatedAsArguments()
    {
        var text = """
            <tool_call>{"name": "read_file", "parameters": {"path": "test.txt"}}</tool_call>
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("read_file", results[0].Name);
        Assert.Equal("test.txt", results[0].Arguments!["path"]?.ToString());
    }

    // ── "function" wrapper format ──

    [Fact]
    public void Parse_FunctionWrapperFormat_ExtractsToolCall()
    {
        var text = """
            <tool_call>{"function": {"name": "bash", "arguments": {"command": "ls"}}}</tool_call>
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("bash", results[0].Name);
    }

    // ── <function_call> tags ──

    [Fact]
    public void Parse_FunctionCallTags_ExtractsToolCall()
    {
        var text = """
            <function_call>{"name": "grep", "arguments": {"pattern": "test"}}</function_call>
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("grep", results[0].Name);
    }

    // ── Edge cases ──

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(_parser.ParseToolCallsFromText(""));
    }

    [Fact]
    public void Parse_NullString_ReturnsEmpty()
    {
        Assert.Empty(_parser.ParseToolCallsFromText(null!));
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmpty()
    {
        Assert.Empty(_parser.ParseToolCallsFromText("   \n  \t  "));
    }

    [Fact]
    public void Parse_PlainTextNoToolCalls_ReturnsEmpty()
    {
        var text = "Hello! I can help you with that. Let me explain how this works.";
        Assert.Empty(_parser.ParseToolCallsFromText(text));
    }

    [Fact]
    public void Parse_InvalidJson_SkipsGracefully()
    {
        var text = """
            <tool_call>
            {this is not valid json at all}
            </tool_call>
            """;

        Assert.Empty(_parser.ParseToolCallsFromText(text));
    }

    [Fact]
    public void Parse_JsonWithoutName_SkipsGracefully()
    {
        var text = """
            <tool_call>
            {"arguments": {"path": "test.txt"}}
            </tool_call>
            """;

        Assert.Empty(_parser.ParseToolCallsFromText(text));
    }

    [Fact]
    public void Parse_GeneratesUniqueCallIds()
    {
        var text = """
            <tool_call>{"name": "a", "arguments": {}}</tool_call>
            <tool_call>{"name": "b", "arguments": {}}</tool_call>
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Equal(2, results.Count);
        Assert.NotEqual(results[0].CallId, results[1].CallId);
        Assert.StartsWith("parsed_", results[0].CallId);
    }

    [Fact]
    public void Parse_BooleanAndNumericArguments_Preserved()
    {
        var text = """
            <tool_call>{"name": "test", "arguments": {"flag": true, "count": 42, "label": null}}</tool_call>
            """;

        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal(true, results[0].Arguments!["flag"]);
        Assert.Equal("42", results[0].Arguments!["count"]?.ToString());
        Assert.Null(results[0].Arguments!["label"]);
    }

    // ── ShouldNudgeForToolCalls tests ──

    [Fact]
    public void Nudge_AlreadyHasToolCallTag_ReturnsFalse()
    {
        var text = """
            <tool_call>{"name": "bash", "arguments": {"command": "ls"}}</tool_call>
            """;
        Assert.False(_parser.ShouldNudgeForToolCalls(text));
    }

    [Fact]
    public void Nudge_CodeBlockWithToolLikeJson_ReturnsTrue()
    {
        var text = """
            I'll create a file for you. Here's what I would do:
            ```json
            {"name": "write_file", "arguments": {"path": "test.txt"}}
            ```
            """;

        // This contains a code block with tool-like JSON but no <tool_call> tags
        Assert.True(_parser.ShouldNudgeForToolCalls(text));
    }

    [Fact]
    public void Nudge_CodeBlockWithActionWordsAndFilePath_ReturnsTrue()
    {
        var text = """
            I'll create the file src/test.cs with this content:
            ```csharp
            Console.WriteLine("hello");
            ```
            """;

        Assert.True(_parser.ShouldNudgeForToolCalls(text));
    }

    [Fact]
    public void Nudge_PlainText_ReturnsFalse()
    {
        var text = "Sure, I can help you understand how that works.";
        Assert.False(_parser.ShouldNudgeForToolCalls(text));
    }

    [Fact]
    public void Nudge_EmptyText_ReturnsFalse()
    {
        Assert.False(_parser.ShouldNudgeForToolCalls(""));
    }

    [Fact]
    public void Nudge_ThinkBlockOnly_ReturnsFalse()
    {
        var text = "<think>I should use a tool here but I'll just think about it.</think>";
        Assert.False(_parser.ShouldNudgeForToolCalls(text));
    }

    // ── ParseArguments tests ──

    [Fact]
    public void ParseArguments_StringValue_ReturnsString()
    {
        var json = System.Text.Json.JsonDocument.Parse("""{"key": "value"}""");
        var result = ToolCallParser.ParseArguments(json.RootElement);
        Assert.Equal("value", result["key"]);
    }

    [Fact]
    public void ParseArguments_BoolValue_ReturnsBool()
    {
        var json = System.Text.Json.JsonDocument.Parse("""{"flag": true, "other": false}""");
        var result = ToolCallParser.ParseArguments(json.RootElement);
        Assert.Equal(true, result["flag"]);
        Assert.Equal(false, result["other"]);
    }

    [Fact]
    public void ParseArguments_NullValue_ReturnsNull()
    {
        var json = System.Text.Json.JsonDocument.Parse("""{"key": null}""");
        var result = ToolCallParser.ParseArguments(json.RootElement);
        Assert.Null(result["key"]);
    }

    // ── Bare JSON regex false positive tests (#98) ──

    [Fact]
    public void Parse_BareJsonWithNestedArguments_ExtractsToolCall()
    {
        var text = """{"name": "bash", "arguments": {"command": "echo", "opts": {"verbose": true}}}""";
        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("bash", results[0].Name);
    }

    [Fact]
    public void Parse_BareJsonWithExtraFieldsBeforeArguments_Skipped()
    {
        // Extra fields between name and arguments — should still not match random JSON
        var text = """Just a plain JSON config: {"host": "localhost", "port": 8080}""";
        var results = _parser.ParseToolCallsFromText(text);

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_RandomJsonWithNameField_NotFalsePositive()
    {
        // JSON with "name" field but no "arguments" — should not match
        var text = """{"name": "John", "age": 30, "city": "NYC"}""";
        var results = _parser.ParseToolCallsFromText(text);

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_BareJsonRequiresNameFirst()
    {
        // "arguments" before "name" — the improved regex requires name before arguments
        var text = """{"arguments": {"path": "test.txt"}, "name": "read_file"}""";
        var results = _parser.ParseToolCallsFromText(text);

        // This should NOT match the bare JSON regex (name must come first),
        // but it wouldn't be found by any other pattern either
        Assert.Empty(results);
    }

    [Fact]
    public void Parse_BareJsonWithWhitespace_ExtractsToolCall()
    {
        var text = """
            {
                "name": "read_file",
                "arguments": {
                    "path": "/tmp/test.txt"
                }
            }
            """;
        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("read_file", results[0].Name);
    }

    [Fact]
    public void Parse_WrappedToolCallFormat_ExtractsToolCall()
    {
        var text = """{"tool_call": {"name": "bash", "arguments": {"command": "ls"}}}""";
        var results = _parser.ParseToolCallsFromText(text);

        Assert.Single(results);
        Assert.Equal("bash", results[0].Name);
    }
}
