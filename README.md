# OpenOrca

[![CI](https://github.com/Open-Orca-AI/openorca/actions/workflows/ci.yml/badge.svg)](https://github.com/Open-Orca-AI/openorca/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)

![OpenOrca Demo](demo/demo.gif)

**OpenOrca** is an autonomous AI coding agent that runs in your terminal. It connects to local LLM servers (LM Studio, Ollama, or any OpenAI-compatible API) and uses 34 built-in tools to read, write, and execute code — not just describe what to do, but actually do it.

Think of it as a local, private, open-source alternative to cloud-based AI coding assistants.

## Features

- **Autonomous agent loop** — the LLM plans, acts, observes results, and iterates up to 25 turns per request
- **34 built-in tools** — file I/O, shell execution, git operations, web search, GitHub integration, network diagnostics, archiving, and more
- **Works with any local model** — Mistral, Llama, DeepSeek, Qwen, or any model served via OpenAI-compatible API
- **Native + text-based tool calling** — auto-detects whether your model supports OpenAI function calling and falls back to text-based `<tool_call>` tags
- **Streaming with live thinking indicator** — see tokens arrive in real-time, or collapse thinking with Ctrl+O
- **Plan mode** — have the model plan before executing, then approve/modify/discard
- **Session management** — auto-save and restore conversations
- **Context management** — auto-compaction when approaching context limits, manual `/compact`
- **Project instructions** — drop an `ORCA.md` file in your project root for persistent instructions
- **Hooks** — run custom shell commands before/after tool execution
- **Permission system** — approve tool calls by risk level (read-only, moderate, dangerous)
- **Sub-agent spawning** — delegate focused tasks to independent agent instances

## Installation

### Download a Release

Download the latest release for your platform from [GitHub Releases](https://github.com/openorca-ai/openorca/releases):

| Platform | File |
|----------|------|
| Windows x64 | `openorca-win-x64.zip` |
| Linux x64 | `openorca-linux-x64.tar.gz` |
| macOS x64 | `openorca-osx-x64.tar.gz` |
| macOS Apple Silicon | `openorca-osx-arm64.tar.gz` |

Extract and add to your PATH.

### Build from Source

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
git clone https://github.com/openorca-ai/openorca.git
cd openorca
dotnet build
dotnet run --project src/OpenOrca.Cli
```

To publish a self-contained executable:

```bash
dotnet publish src/OpenOrca.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# Output: src/OpenOrca.Cli/bin/Release/net9.0/win-x64/publish/openorca.exe
```

## Quick Start

1. **Start LM Studio** and load a model (e.g., Mistral 7B Instruct v0.3)
2. **Enable the local server** in LM Studio (default: `http://localhost:1234/v1`)
3. **Run OpenOrca:**

```bash
openorca
```

4. **Ask it to do something:**

```
> Create a Python Flask API with /health and /users endpoints
```

OpenOrca will create files, install dependencies, and verify the result — all autonomously.

### Single-Prompt Mode

For CI/scripting, pass a prompt directly:

```bash
openorca --prompt "List all .cs files in this project"
```

## Configuration

Config is stored at `~/.openorca/config.json`. Edit interactively with `/config` or modify the file directly.

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
    "disabledTools": []
  },
  "context": {
    "contextWindowSize": 8192,
    "autoCompactThreshold": 0.8,
    "compactPreserveLastN": 4,
    "autoCompactEnabled": true
  },
  "session": {
    "autoSave": true,
    "maxSessions": 100
  },
  "hooks": {
    "preToolHooks": {},
    "postToolHooks": {}
  }
}
```

### Model Compatibility

| Model | Native Tool Calling | Notes |
|-------|:-------------------:|-------|
| Mistral 7B Instruct v0.3 | Yes | Best tested. Set `nativeToolCalling: true` |
| DeepSeek R1 Distill Qwen 7B | No | Uses `<think>` tags + bare JSON tool calls |
| Llama 3 / 3.1 | Varies | Test with your specific quantization |
| Any OpenAI-compatible | Varies | Auto-fallback to text-based tool calling |

## Architecture

```
OpenOrca.sln
├── src/
│   ├── OpenOrca.Cli          # Console app, REPL, streaming UI
│   │   ├── Repl/             # ReplLoop, CommandHandler, AgentLoopRunner, etc.
│   │   └── Rendering/        # StreamingRenderer, ThinkingIndicator, ToolCallRenderer
│   ├── OpenOrca.Core          # Domain logic
│   │   ├── Chat/             # ConversationManager, Conversation
│   │   ├── Client/           # LmStudioClientFactory, ModelDiscovery
│   │   ├── Configuration/    # OrcaConfig, ConfigManager, PromptManager
│   │   ├── Hooks/            # HookRunner
│   │   ├── Orchestration/    # AgentOrchestrator
│   │   ├── Permissions/      # PermissionManager
│   │   └── Session/          # SessionManager
│   └── OpenOrca.Tools         # 34 tool implementations
│       ├── FileSystem/       # read_file, write_file, edit_file, glob, grep, etc.
│       ├── Shell/            # bash, background processes
│       ├── Git/              # git_status, git_commit, git_push, etc.
│       ├── GitHub/           # GitHub CLI wrapper
│       ├── Web/              # web_fetch, web_search
│       ├── Network/          # network_diagnostics
│       ├── Archive/          # archive (zip create/extract/list)
│       ├── Interactive/      # ask_user
│       ├── Utility/          # think, task_list, env
│       └── Agent/            # spawn_agent
└── tests/
    ├── OpenOrca.Cli.Tests     # CLI and REPL unit tests
    ├── OpenOrca.Core.Tests    # Core domain unit tests
    ├── OpenOrca.Tools.Tests   # Tool unit tests
    └── OpenOrca.Harness       # Integration tests (requires LM Studio)
```

## Tools

### File System (11 tools)

| Tool | Risk | Description |
|------|------|-------------|
| `read_file` | ReadOnly | Read file contents with line numbers |
| `write_file` | Moderate | Create or overwrite files |
| `edit_file` | Moderate | Exact string replacement in files |
| `delete_file` | Moderate | Delete files or directories |
| `copy_file` | Moderate | Copy files or directories |
| `move_file` | Moderate | Move or rename files |
| `mkdir` | Moderate | Create directories |
| `cd` | Moderate | Change working directory |
| `glob` | ReadOnly | Find files by pattern |
| `grep` | ReadOnly | Search file contents with regex |
| `list_directory` | ReadOnly | List directory contents |

### Shell (4 tools)

| Tool | Risk | Description |
|------|------|-------------|
| `bash` | Dangerous | Execute shell commands |
| `start_background_process` | Dangerous | Start long-running processes |
| `get_process_output` | ReadOnly | Read background process output |
| `stop_process` | Moderate | Stop background processes |

### Git (9 tools)

| Tool | Risk | Description |
|------|------|-------------|
| `git_status` | ReadOnly | Working tree status |
| `git_diff` | ReadOnly | Show changes |
| `git_log` | ReadOnly | Commit history |
| `git_commit` | Moderate | Stage and commit |
| `git_branch` | Moderate | List/create/delete branches |
| `git_checkout` | Moderate | Switch branches |
| `git_push` | Dangerous | Push to remote |
| `git_pull` | Moderate | Pull from remote |
| `git_stash` | Moderate | Stash/restore changes |

### Web & GitHub (3 tools)

| Tool | Risk | Description |
|------|------|-------------|
| `web_fetch` | ReadOnly | Fetch URL content |
| `web_search` | ReadOnly | Search with DuckDuckGo |
| `github` | Moderate | GitHub CLI operations |

### Network & Archive (2 tools)

| Tool | Risk | Description |
|------|------|-------------|
| `network_diagnostics` | ReadOnly | Ping, DNS lookup, or HTTP connectivity check |
| `archive` | Moderate | Create, extract, or list zip archives |

### Utility & Interactive (5 tools)

| Tool | Risk | Description |
|------|------|-------------|
| `think` | ReadOnly | Step-by-step reasoning |
| `task_list` | ReadOnly | Track progress on tasks |
| `env` | ReadOnly | Inspect environment variables |
| `ask_user` | ReadOnly | Ask user a question |
| `spawn_agent` | Moderate | Launch a sub-agent |

## Slash Commands

| Command | Description |
|---------|-------------|
| `/help`, `/h`, `/?` | Show help |
| `/clear`, `/c` | Clear conversation |
| `/model [name]` | List or set model |
| `/config` | Interactive configuration editor |
| `/session list\|save\|load\|delete` | Manage sessions |
| `/plan`, `/p [on\|off]` | Toggle plan mode |
| `/compact [instructions]` | Compact conversation context |
| `/rewind [N]` | Remove last N turns |
| `/context`, `/ctx` | Show context window usage |
| `/stats`, `/cost` | Session statistics |
| `/memory [show\|edit]` | View/edit ORCA.md project instructions |
| `/doctor`, `/diag` | Run diagnostic checks |
| `/copy`, `/cp` | Copy last response to clipboard |
| `/export [path]` | Export conversation to markdown |
| `!<command>` | Run shell command directly |
| `/exit`, `/quit`, `/q` | Exit |

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+O` | Toggle thinking output visibility |
| `Ctrl+C` | Cancel generation (first press) / Exit (second press within 2s) |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, code style, and PR process.

## License

[MIT](LICENSE)
