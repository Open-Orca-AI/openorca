# Configuration

OpenOrca configuration is stored at `~/.openorca/config.json`. Edit it directly, or use `/config` in the REPL for an interactive editor.

## Default Configuration

```json
{
  "lmStudio": {
    "baseUrl": "http://localhost:1234/v1",
    "apiKey": "lm-studio",
    "model": null,
    "temperature": 0.7,
    "maxTokens": null,
    "timeoutSeconds": 120,
    "streamingTimeoutSeconds": 120,
    "nativeToolCalling": false,
    "promptProfile": null
  },
  "permissions": {
    "autoApproveAll": false,
    "autoApproveReadOnly": true,
    "autoApproveModerate": false,
    "alwaysApprove": [],
    "disabledTools": [],
    "allowPatterns": [],
    "denyPatterns": []
  },
  "context": {
    "contextWindowSize": 8192,
    "autoCompactThreshold": 0.8,
    "compactPreserveLastN": 4,
    "autoCompactEnabled": true,
    "charsPerToken": 3.5
  },
  "session": {
    "autoSave": true,
    "maxSessions": 100
  },
  "agent": {
    "maxIterations": 15,
    "timeoutSeconds": 300
  },
  "hooks": {
    "preToolHooks": {},
    "postToolHooks": {}
  },
  "memory": {
    "autoMemoryEnabled": true,
    "maxMemoryFiles": 20
  }
}
```

---

## LM Studio Settings (`lmStudio`)

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `baseUrl` | string | `http://localhost:1234/v1` | LLM server endpoint. Works with LM Studio, Ollama, or any OpenAI-compatible API. |
| `apiKey` | string | `lm-studio` | API key. LM Studio doesn't require a real key — any string works. Change this if your server requires authentication. |
| `model` | string | `null` | Model ID to use. If `null`, auto-detects (uses the only loaded model, or the first one). Set this explicitly when multiple models are loaded. |
| `temperature` | float | `0.7` | Sampling temperature. Lower = more focused, higher = more creative. `0.7` is a good default for coding tasks. |
| `maxTokens` | int? | `null` | Max tokens per response. `null` lets the server decide. Set this if responses are being cut off. |
| `timeoutSeconds` | int | `120` | HTTP request timeout. Increase for slower hardware or larger models. |
| `streamingTimeoutSeconds` | int | `120` | Idle timeout per streaming response. Resets on each token received. Increase for models that pause during generation. |
| `nativeToolCalling` | bool | `false` | Send tool definitions via OpenAI function calling protocol. Set to `true` for models that support it (e.g., Mistral). See [Model Setup](Model-Setup). |
| `promptProfile` | string? | `null` | Override which prompt template to use. See [Model Setup](Model-Setup#prompt-profiles). |

## Permission Settings (`permissions`)

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `autoApproveAll` | bool | `false` | Auto-approve all tool calls regardless of risk level. **Use with caution** — this includes shell execution and git push. |
| `autoApproveReadOnly` | bool | `true` | Auto-approve ReadOnly tools (file reading, searching, thinking). Safe to leave on. |
| `autoApproveModerate` | bool | `false` | Auto-approve Moderate tools (file writing, git commit, mkdir). Useful for trusted workflows. |
| `alwaysApprove` | string[] | `[]` | List of specific tool names to always auto-approve, regardless of risk level. Example: `["bash", "write_file"]` |
| `disabledTools` | string[] | `[]` | List of tool names to completely disable. These tools won't appear in the system prompt or be callable. |
| `allowPatterns` | string[] | `[]` | Glob patterns for auto-approving specific tool+argument combinations. Format: `ToolName(argGlob)`. Example: `Bash(git *)` auto-approves all git commands. |
| `denyPatterns` | string[] | `[]` | Glob patterns for blocking specific tool+argument combinations. **Deny takes priority over allow.** Example: `Bash(rm -rf *)` blocks all `rm -rf` commands. |

### Permission Glob Patterns

Permission glob patterns provide fine-grained control over tool approvals based on the command or file path being used. Patterns use the format `ToolName(argGlob)`.

**Pattern syntax:**
- `*` matches any characters within a single path segment
- `**` matches any characters across path segments (for file paths)
- Tool names are matched case-insensitively

**How it works:**
1. Deny patterns are checked first — if any match, the tool call is blocked
2. Allow patterns are checked next — if any match, the tool call is auto-approved
3. If no pattern matches, the standard permission flow applies

**Argument extraction by tool type:**
| Tool | Argument used for matching |
|------|---------------------------|
| `bash` | The `command` property |
| `write_file`, `edit_file`, `read_file`, `delete_file`, `copy_file`, `move_file`, `multi_edit` | The `path` property |

**Examples:**
```json
{
  "permissions": {
    "allowPatterns": [
      "Bash(git *)",
      "Bash(dotnet *)",
      "write_file(src/**)",
      "edit_file(src/**)"
    ],
    "denyPatterns": [
      "Bash(rm -rf *)",
      "Bash(sudo *)",
      "write_file(.env*)"
    ]
  }
}
```

### CLI Override: `--allow`

You can pre-approve tools from the command line without modifying your config. This is useful for CI/CD pipelines:

```bash
openorca --prompt "Run the tests" --allow bash,read_file,grep
```

The `--allow` flag appends to `alwaysApprove` for that session only — it doesn't modify `config.json`.

### Permission Examples

**Fully autonomous (trust everything):**
```json
{
  "permissions": {
    "autoApproveAll": true
  }
}
```

**Moderate trust (auto-approve file writes but confirm shell/push):**
```json
{
  "permissions": {
    "autoApproveReadOnly": true,
    "autoApproveModerate": true
  }
}
```

**Specific tool trust:**
```json
{
  "permissions": {
    "alwaysApprove": ["write_file", "edit_file", "bash"]
  }
}
```

**Disable tools you don't want:**
```json
{
  "permissions": {
    "disabledTools": ["git_push", "spawn_agent"]
  }
}
```

## Context Settings (`context`)

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `contextWindowSize` | int | `8192` | Total context window in tokens. Should match your model's actual window size. Common values: 4096, 8192, 16384, 32768. |
| `autoCompactThreshold` | float | `0.8` | Trigger auto-compaction when context usage exceeds this percentage (0.0–1.0). At 0.8, compaction triggers at 80% usage. |
| `compactPreserveLastN` | int | `4` | Number of recent conversation turns to keep when compacting. These are never summarized. |
| `autoCompactEnabled` | bool | `true` | Enable automatic context compaction. If `false`, you must manually use `/compact`. |
| `charsPerToken` | float | `3.5` | Characters-per-token ratio for context usage estimation. Adjust if your model's tokenizer differs significantly. |

**Tip:** Set `contextWindowSize` to match your model's actual context window (check LM Studio model card). If set too high, the model may receive truncated context without warning. If set too low, compaction triggers too often.

## Session Settings (`session`)

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `autoSave` | bool | `true` | Automatically save the conversation when exiting. |
| `maxSessions` | int | `100` | Maximum number of saved sessions. Oldest sessions are pruned when this limit is exceeded. |

## Agent Settings (`agent`)

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `maxIterations` | int | `15` | Maximum iterations for sub-agent loops spawned via `spawn_agent`. |
| `timeoutSeconds` | int | `300` | Timeout in seconds for sub-agent tasks. |

## Hook Settings (`hooks`)

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `preToolHooks` | object | `{}` | Shell commands to run before a tool executes. Key is tool name or `*` for all. Non-zero exit blocks the tool. |
| `postToolHooks` | object | `{}` | Shell commands to run after a tool executes. Key is tool name or `*` for all. Fire-and-forget — exit code doesn't affect anything. |

See [Hooks & Extensibility](Hooks-and-Extensibility) for details and examples.

## Memory Settings (`memory`)

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `autoMemoryEnabled` | bool | `true` | Automatically save session learnings at session end and load them into the system prompt for future sessions. |
| `maxMemoryFiles` | int | `20` | Maximum number of auto-generated memory files. Oldest files are pruned when this limit is exceeded. |

When enabled, at the end of each session that includes meaningful tool usage, OpenOrca asks the LLM to summarize project-specific patterns and learnings. These are saved as `.md` files in:
- **Project memory:** `.orca/memory/` (takes priority)
- **Global memory:** `~/.openorca/memory/`

Memory content is loaded into the system prompt at session start. Manage memory with the `/memory` command:
- `/memory list` — list all memory files
- `/memory auto on|off` — toggle auto memory
- `/memory clear-auto` — delete all auto-generated memory files

See [Auto Memory](Auto-Memory) for details.

---

## Data Directory Structure

All OpenOrca data lives under `~/.openorca/`:

```
~/.openorca/
├── config.json            # Main configuration
├── logs/
│   └── openorca-2025-01-15.log   # Daily log files
├── prompts/
│   ├── default.md         # Default system prompt template
│   └── mistral-7b-instruct-v0.3.md  # Auto-generated model-specific prompt
├── sessions/
│   ├── abc123.json         # Saved conversation sessions
│   └── ...
├── checkpoints/            # File checkpoints (per session)
│   └── {sessionId}/
│       ├── manifest.json   # Checkpoint manifest
│       └── *.bak           # Original file snapshots
├── memory/                 # Global auto-learned memory files
│   └── 20260221-a1b2c3.md
└── commands/               # Global custom slash commands
    └── my-command.md
```

### Project-Level Data

Projects can also have local data in `.orca/`:

```
.orca/
├── ORCA.md                 # Project instructions
├── commands/               # Project-level custom slash commands
│   └── review-pr.md
└── memory/                 # Project-level auto-learned memory
    └── 20260221-d4e5f6.md
```
