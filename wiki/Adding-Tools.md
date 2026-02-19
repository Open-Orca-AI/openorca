# Adding Tools

This guide walks through creating a new tool for OpenOrca, from implementation to testing.

## Overview

Every tool in OpenOrca:
1. Implements the `IOrcaTool` interface
2. Has an `[OrcaTool("tool_name")]` attribute
3. Is auto-discovered by `ToolRegistry.DiscoverTools()` at startup
4. Lives in `src/OpenOrca.Tools/` under the appropriate category folder

No manual registration is needed — just implement and build.

## Step-by-Step Example: `word_count` Tool

Let's create a tool that counts words, lines, and characters in a file.

### 1. Create the Tool File

Create `src/OpenOrca.Tools/Utility/WordCountTool.cs`:

```csharp
using System.Text.Json;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.Utility;

[OrcaTool("word_count")]
public sealed class WordCountTool : IOrcaTool
{
    public string Name => "word_count";

    public string Description => "Count words, lines, and characters in a file.";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Path to the file to count"
                }
            },
            "required": ["path"]
        }
        """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var path = args.GetProperty("path").GetString()
            ?? throw new ArgumentException("path is required");

        if (!File.Exists(path))
            return ToolResult.Error($"File not found: {path}");

        var content = await File.ReadAllTextAsync(path, ct);
        var lines = content.Split('\n').Length;
        var words = content.Split([ ' ', '\t', '\n', '\r' ],
            StringSplitOptions.RemoveEmptyEntries).Length;
        var chars = content.Length;

        return ToolResult.Success($"Lines: {lines}\nWords: {words}\nCharacters: {chars}");
    }
}
```

### 2. Key Decisions

#### Risk Level

Choose the appropriate risk level:

| Level | When to Use | Examples |
|-------|-------------|---------|
| `ReadOnly` | No side effects, only reads data | `read_file`, `glob`, `grep`, `think` |
| `Moderate` | Modifies state but is recoverable | `write_file`, `git_commit`, `mkdir` |
| `Dangerous` | Potentially destructive or irreversible | `bash`, `git_push`, `start_background_process` |

Our `word_count` tool only reads a file, so `ReadOnly` is correct.

#### Parameter Schema

The `ParameterSchema` is a JSON Schema object that describes the tool's parameters. This is used:
- In native tool calling mode: sent to the model as the function definition
- In text-based mode: included in the system prompt description
- For validation hints (though OpenOrca doesn't strictly validate — the tool itself handles bad input)

Tips:
- Always include `"description"` on each property — the model reads these
- Use `"required"` to mark mandatory parameters
- Keep descriptions concise but clear
- Use `"enum"` for parameters with a fixed set of values

#### ToolResult

Return values:
- `ToolResult.Success(string content)` — successful result, shown to the model
- `ToolResult.Error(string message)` — error result, shown to the model (it can retry)

Keep results concise. The model processes the full result text, so very long outputs waste context. Truncate if needed.

### 3. Add Tests

Create `tests/OpenOrca.Tools.Tests/Utility/WordCountToolTests.cs`:

```csharp
using System.Text.Json;
using OpenOrca.Tools.Utility;

namespace OpenOrca.Tools.Tests.Utility;

public sealed class WordCountToolTests
{
    private readonly WordCountTool _tool = new();

    [Fact]
    public void Name_Is_Correct()
    {
        Assert.Equal("word_count", _tool.Name);
    }

    [Fact]
    public void RiskLevel_Is_ReadOnly()
    {
        Assert.Equal(ToolRiskLevel.ReadOnly, _tool.RiskLevel);
    }

    [Fact]
    public async Task ExecuteAsync_Counts_File_Contents()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "hello world\nfoo bar baz\n");

            var args = JsonDocument.Parse($"""
                {{"path": "{tempFile.Replace("\\", "\\\\")}"}}
                """).RootElement;

            var result = await _tool.ExecuteAsync(args);

            Assert.False(result.IsError);
            Assert.Contains("Lines: 3", result.Content);
            Assert.Contains("Words: 5", result.Content);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_For_Missing_File()
    {
        var args = JsonDocument.Parse("""
            {"path": "/nonexistent/file.txt"}
            """).RootElement;

        var result = await _tool.ExecuteAsync(args);

        Assert.True(result.IsError);
        Assert.Contains("File not found", result.Content);
    }
}
```

### 4. Build and Test

```bash
dotnet build
dotnet test
```

The tool is now available in OpenOrca. Start a new session and the model will see it in its tool list.

## Tools That Need CLI Wiring

Most tools are self-contained — they just need the `IOrcaTool` interface. However, some tools require integration with the CLI layer:

- **`ask_user`** — needs access to console input (injected via constructor)
- **`spawn_agent`** — needs access to the agent orchestrator

If your tool needs access to services beyond its own scope, you may need to:
1. Add constructor parameters for the required services
2. Update the tool registration in `Program.cs` if auto-discovery can't resolve the dependencies

Check existing tools like `AskUserTool` or `SpawnAgentTool` for examples of dependency injection.

## Common Pitfalls

1. **Don't return huge results** — the entire result goes into the conversation context. Truncate long outputs (the `read_file` tool caps at ~500 lines by default).

2. **Handle cancellation** — pass `CancellationToken` through to async operations. The user can cancel with Ctrl+C.

3. **Use `ToolResult.Error` for expected failures** — don't throw exceptions for things like "file not found". Return a `ToolResult.Error` so the model can see the error and adjust.

4. **Throw for unexpected failures** — unexpected exceptions are caught by the executor and shown as errors with stack traces in the logs.

5. **Keep parameter names consistent** — use `path` for file paths, `command` for shell commands, `pattern` for search patterns. Look at existing tools for conventions.

6. **Use raw string literals for JSON schemas** — use `"""..."""` (C# raw string literals) for the parameter schema to avoid escaping issues. But remember: raw interpolated strings (`$"""..."""`) cannot contain literal `{` braces — extract the schema to a non-interpolated `const string` if you need interpolation elsewhere.
