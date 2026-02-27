<p align="center">
  <img src="https://raw.githubusercontent.com/Open-Orca-AI/openorca/main/assets/orca_mascot.png" alt="OpenOrca Mascot" width="150">
</p>

# OpenOrca

**OpenOrca** is an autonomous AI coding agent that runs in your terminal. It connects to local LLM servers (LM Studio, Ollama, or any OpenAI-compatible API) and uses 39 built-in tools to read, write, and execute code — not just describe what to do, but actually do it.

Think of it as a local, private, open-source alternative to cloud-based AI coding assistants.

## Key Capabilities

- **Autonomous agent loop** — the LLM plans, acts, observes results, and iterates up to 25 turns per request
- **39 built-in tools** — file I/O, shell execution, git operations, web search, GitHub integration, and more
- **Works with any local model** — Mistral, Llama, DeepSeek, Qwen, or any model served via OpenAI-compatible API
- **Native + text-based tool calling** — auto-detects whether your model supports function calling and falls back gracefully
- **Streaming with live thinking** — see tokens arrive in real-time, or collapse thinking with Ctrl+O
- **Plan mode** — have the model plan before executing, then approve/modify/discard
- **Session management** — auto-save and restore conversations
- **Context management** — auto-compaction when approaching context limits
- **Project instructions** — drop an `ORCA.md` file in your project root for persistent instructions
- **Custom slash commands** — create `.orca/commands/*.md` files to define project-specific commands
- **File checkpoints** — automatic snapshots before file edits, with `/checkpoint` to list, diff, and restore
- **Auto memory** — session learnings are saved and loaded into future sessions automatically
- **Permission glob patterns** — fine-grained allow/deny rules like `Bash(git *)` and `Write(src/**)`
- **Hooks** — run custom shell commands before/after tool execution
- **Permission system** — approve tool calls by risk level
- **Sub-agent spawning** — delegate focused tasks to independent agent instances

## Supported Platforms

| Platform | File |
|----------|------|
| Windows x64 | `openorca-win-x64.zip` |
| Linux x64 | `openorca-linux-x64.tar.gz` |
| macOS x64 | `openorca-osx-x64.tar.gz` |
| macOS Apple Silicon | `openorca-osx-arm64.tar.gz` |

## Where to Start

| I want to... | Go to... |
|--------------|----------|
| Install and run OpenOrca for the first time | [Installation](Installation) > [Quick Start](Quick-Start) |
| Understand which models work | [Model Setup](Model-Setup) |
| Learn all commands and tools | [Commands & Shortcuts](Commands-and-Shortcuts) > [Tool Reference](Tool-Reference) |
| Configure OpenOrca | [Configuration](Configuration) |
| Troubleshoot a problem | [Troubleshooting](Troubleshooting) |
| Contribute or extend OpenOrca | [Architecture](Architecture) > [Adding Tools](Adding-Tools) > [Contributing](Contributing) |
