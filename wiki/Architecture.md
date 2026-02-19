# Architecture

This page describes OpenOrca's internal architecture for developers who want to understand, extend, or contribute to the codebase.

## Solution Structure

```
OpenOrca.sln
├── src/
│   ├── OpenOrca.Cli          # Console app, REPL, streaming UI
│   ├── OpenOrca.Core         # Domain logic (chat, config, sessions, permissions, hooks)
│   └── OpenOrca.Tools        # 31 tool implementations
└── tests/
    ├── OpenOrca.Core.Tests   # 49 unit tests
    ├── OpenOrca.Tools.Tests  # 65 unit tests
    └── OpenOrca.Harness      # Integration tests (requires LM Studio)
```

## Dependency Graph

```
OpenOrca.Cli
  ├── OpenOrca.Core
  └── OpenOrca.Tools
        └── OpenOrca.Core
```

- **Cli** depends on both Core and Tools
- **Tools** depends on Core (for config, permissions)
- **Core** has no project dependencies (only NuGet: Microsoft.Extensions.AI, System.Text.Json, etc.)

## OpenOrca.Cli

The console application, REPL loop, and all rendering/UI concerns.

### Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point. Parses CLI args (`--prompt`, `--demo`), loads config, creates services, starts REPL. |
| `Repl/ReplLoop.cs` | Main REPL loop. Reads user input, dispatches to CommandHandler or AgentLoopRunner. |
| `Repl/AgentLoopRunner.cs` | Runs the agent loop: streaming, native/text tool switching, retry logic, auto-compaction, server error probing, nudge, and generation cancellation. Max 25 iterations per user message. |
| `Repl/CommandHandler.cs` | Handles all slash commands (`/help`, `/clear`, `/model`, `/session`, `/plan`, `/compact`, `/rewind`, `/context`, `/stats`, `/memory`, `/doctor`, `/copy`, `/export`). |
| `Repl/ToolCallParser.cs` | Parses tool calls from LLM text output. Handles `<tool_call>` tags, `<\|tool_call\|>` tags, `[TOOL_CALL]` tags, `<function_call>` tags, JSON in code fences, bare JSON, and `{"function": {...}}` wrappers. |
| `Repl/ToolCallExecutor.cs` | Executes parsed tool calls: permission checks, hook running, tool invocation, result formatting. |
| `Repl/SystemPromptBuilder.cs` | Constructs the system prompt from templates, substituting `{{TOOL_LIST}}`, `{{CWD}}`, `{{PLATFORM}}`, `{{PROJECT_INSTRUCTIONS}}`. |
| `Repl/ReplState.cs` | Mutable state: plan mode, show thinking, session ID, last response, token counts, stopwatch. |
| `Rendering/StreamingRenderer.cs` | Renders streaming tokens to the terminal with markdown-like formatting. |
| `Rendering/ThinkingIndicator.cs` | Shows animated thinking indicator and token counter when thinking is hidden. |
| `Rendering/ToolCallRenderer.cs` | Renders tool call panels (name, arguments, result, timing). |

### Agent Loop Flow

The core of OpenOrca is the agent loop in `AgentLoopRunner.RunAgentLoopAsync()`:

```
User sends message
       │
       ▼
┌─── Agent Loop (max 25 iterations) ◄───────────────────┐
│      │                                                  │
│      ▼                                                  │
│  Auto-compact check                                     │
│      │                                                  │
│      ▼                                                  │
│  Stream LLM response                                    │
│      │                                                  │
│      ├── Native tool calls found?                       │
│      │    ├── Yes ─► Execute tools ─► Add results ──────┘
│      │    │          (with permission + hooks)
│      │    └── No
│      │         │
│      │         ▼
│      ├── Text tool calls parsed?                        │
│      │    ├── Yes ─► Execute tools ─► Add results ──────┘
│      │    └── No
│      │         │
│      │         ▼
│      ├── Truncated <tool_call>? ─► Nudge continue ──────┘
│      │         │
│      │         ▼
│      ├── Should nudge? ─► Send nudge message ───────────┘
│      │         │
│      │         ▼
│      └── No tool calls ─► Done (break)
│
└── Retry loop detection (4 identical failures ─► break)
```

### Streaming with Thinking Toggle

During streaming, `Ctrl+O` toggles thinking visibility in real-time:
- **Visible:** Streaming tokens appear in cyan via `StreamingRenderer`
- **Hidden:** `ThinkingIndicator` shows token counter; console output is redirected to `TextWriter.Null`

The thinking state toggle works mid-stream: when toggled on, buffered tokens are flushed to the console.

### Native Tool Auto-Downgrade

When `nativeToolCalling` is `true`:
1. First attempt: send tool definitions via OpenAI function calling protocol
2. If streaming returns updates but 0 content items → retry without tool definitions
3. If native tool calls have missing required arguments → switch to text-based mode
4. Once downgraded, stays in text-based mode for the rest of that agent loop

## OpenOrca.Core

Domain logic with no UI concerns. Can be referenced independently.

### Key Files

| File | Purpose |
|------|---------|
| `Chat/Conversation.cs` | Message list management, system prompt, token estimation, compaction, turn removal. |
| `Chat/ConversationManager.cs` | Tracks active conversations by ID. |
| `Client/LmStudioClientFactory.cs` | Creates `IChatClient` instances configured from `OrcaConfig`. |
| `Client/ModelDiscovery.cs` | Queries the LLM server's `/v1/models` endpoint for available models. |
| `Configuration/OrcaConfig.cs` | Configuration POCO: `LmStudioConfig`, `PermissionsConfig`, `ContextConfig`, `SessionConfig`, `HooksConfig`. |
| `Configuration/ConfigManager.cs` | Loads/saves config.json. Handles `~/.openorca/` directory creation. |
| `Configuration/PromptManager.cs` | Loads and generates system prompt templates. Three-tier resolution: explicit profile → model-specific → default. |
| `Configuration/ProjectInstructionsLoader.cs` | Finds and loads ORCA.md from the project root. |
| `Hooks/HookRunner.cs` | Runs pre/post-tool shell hooks. Pre-hooks can block tool execution (non-zero exit). Post-hooks are fire-and-forget. |
| `Orchestration/AgentOrchestrator.cs` | Manages sub-agent spawning and lifecycle. |
| `Permissions/PermissionManager.cs` | Evaluates whether a tool call should be auto-approved or requires user confirmation. |
| `Session/SessionManager.cs` | Save, load, list, delete conversation sessions as JSON files. |

## OpenOrca.Tools

All 31 tool implementations, organized by category.

### Directory Structure

```
OpenOrca.Tools/
├── Abstractions/
│   ├── IOrcaTool.cs          # Tool interface
│   ├── OrcaToolAttribute.cs  # [OrcaTool("name")] attribute
│   ├── ToolRegistry.cs       # Auto-discovery via reflection
│   ├── ToolResult.cs         # Result type (content + isError)
│   └── ToolRiskLevel.cs      # ReadOnly, Moderate, Dangerous
├── FileSystem/               # read_file, write_file, edit_file, delete_file,
│                             # copy_file, move_file, mkdir, cd, glob, grep, list_directory
├── Shell/                    # bash, start_background_process, get_process_output, stop_process
├── Git/                      # git_status, git_diff, git_log, git_commit, git_branch,
│                             # git_checkout, git_push, git_pull, git_stash
├── GitHub/                   # github (gh CLI wrapper)
├── Web/                      # web_fetch, web_search
├── Interactive/              # ask_user
├── Utility/                  # think, task_list
└── Agent/                    # spawn_agent
```

### Tool Interface

Every tool implements `IOrcaTool`:

```csharp
public interface IOrcaTool
{
    string Name { get; }
    string Description { get; }
    ToolRiskLevel RiskLevel { get; }
    JsonElement ParameterSchema { get; }
    Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct = default);
}
```

### Auto-Discovery

`ToolRegistry.DiscoverTools()` uses reflection to find all classes with the `[OrcaTool]` attribute. No manual registration needed — just implement the interface and add the attribute.

## Data Flow: A User Prompt

Here's the complete path a user prompt takes through the system:

1. **ReplLoop** reads user input → checks for `/` commands or `!` shell shortcut
2. **ReplLoop** adds user message to **Conversation** → calls **AgentLoopRunner**
3. **AgentLoopRunner** checks context usage → may call **CommandHandler.CompactConversationAsync**
4. **AgentLoopRunner** calls `IChatClient.GetStreamingResponseAsync()` with messages and options
5. **StreamingRenderer** displays tokens as they arrive; **ThinkingIndicator** shows counter if thinking is hidden
6. After streaming completes, **AgentLoopRunner** checks for native **FunctionCallContent** items
7. If none, **ToolCallParser** searches the text for tool call patterns
8. **ToolCallExecutor** runs each tool call:
   a. **PermissionManager** checks approval
   b. **HookRunner** runs pre-hooks (can block)
   c. **IOrcaTool.ExecuteAsync** runs the tool
   d. **HookRunner** runs post-hooks
   e. Result is added to **Conversation**
9. Loop repeats from step 4 (up to 25 times)
10. When no tool calls are found, the loop ends and **ReplLoop** prompts for the next input

## Key Design Patterns

- **Separation of concerns:** UI (Cli) vs logic (Core) vs tools (Tools) — clean project boundaries
- **Interface-based tools:** `IOrcaTool` with attribute-based discovery enables easy extension
- **Graceful degradation:** Native tool calling → text-based fallback → nudge → retry loop detection
- **Linked cancellation:** `_generationCts` linked to app CTS allows per-generation Ctrl+C without killing the app
- **Fire-and-forget hooks:** Post-tool hooks never block the agent loop
- **Token estimation:** Simple char/4 heuristic for context tracking (good enough for compaction decisions)
