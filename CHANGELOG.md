# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2025-02-19

### Changed
- **Refactored ReplLoop.cs** from 2,761 lines into 8 focused classes:
  - `PromptConstants` — constant prompt templates
  - `ReplState` — shared mutable session state
  - `ToolCallParser` — text-based tool call parsing and nudge detection
  - `SystemPromptBuilder` — system prompt assembly with project instructions
  - `ConfigEditor` — interactive configuration UI
  - `ToolCallExecutor` — tool execution with plan mode and retry detection
  - `CommandHandler` — slash command dispatch and handlers
  - `AgentLoopRunner` — streaming agent loop with native/text switching
- Removed `desiredTools.md` logging (development-only feature)
- Added logging to previously bare `catch` blocks in `SessionManager`
- Fixed `ConfigManagerTests` to not depend on local config state

### Added
- Open-source publication preparation:
  - MIT license
  - Full README with installation, configuration, architecture, and tool reference
  - Contributing guide
  - Code of Conduct (Contributor Covenant v2.1)
  - EditorConfig for consistent code style
  - GitHub Actions CI (Ubuntu, Windows, macOS)
  - GitHub Actions release workflow (4-platform builds)
  - Issue and PR templates

## [0.2.5] - 2025-02-17

### Added
- **Ctrl+C two-tier cancellation** — first press cancels generation, second exits
- **`/compact [instructions]`** — summarize conversation via LLM to reduce context
- **Auto-compaction** — triggers when context exceeds configurable threshold
- **`/rewind [N]`** — remove last N conversation turns
- **`!<command>`** — bash shortcut to run shell commands directly
- **`/context`, `/ctx`** — show context window usage with visual bar
- **`/stats`, `/cost`** — session statistics (duration, turns, tokens)
- **`/memory [show|edit]`** — view or edit ORCA.md project instructions
- **`/doctor`, `/diag`** — diagnostic checks (connection, model, tools, config)
- **`/copy`, `/cp`** — copy last response to clipboard
- **`/export [path]`** — export conversation to markdown
- **Hooks system** — pre/post-tool hooks via config
- **ORCA.md** — project-level instructions loaded from `.orca/ORCA.md` or `ORCA.md`
- **31 tools** across 8 categories (file system, shell, git, GitHub, web, interactive, utility, agent)
- **Permission system** with three risk levels (ReadOnly, Moderate, Dangerous)
- **Plan mode** — have the model plan before executing, review and approve
- **Native + text-based tool calling** with auto-downgrade on streaming failures
- **Sub-agent spawning** for parallel task execution
- **Session auto-save and restore**
- **Ctrl+O** — toggle thinking output visibility during streaming
- **`--prompt "..."`** — single-prompt mode for testing and CI
