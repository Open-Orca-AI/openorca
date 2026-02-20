# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.3] - 2025-02-20

### Changed
- **CI: optimised demo recording pipeline** — record at 4x speed and slow down with ffmpeg for natural playback
- **CI: add NuGet caching** and Windows Defender exclusion to speed up builds
- **CI: split build/test** and cross-compile releases for faster pipelines

## [0.3.2] - 2025-02-19

### Added
- **Comprehensive GitHub wiki** — 16 pages covering installation, configuration, architecture, tool reference, troubleshooting, and more
- **Wiki publish workflow** — auto-publishes wiki on releases
- **Demo GIF pipeline** — automated terminal recording with VHS, embedded mock client (no external Python server needed)
- **Demo trigger from PR** — checkbox in PR description triggers GIF regeneration

### Fixed
- **`/context` markup error** — fixed rendering bug in context display
- Wait for CI before demo GIF generation

## [0.3.1] - 2025-02-19

### Added
- **3 new tools** (31 → 34 total):
  - `env` — inspect environment variables (get/list with prefix filtering)
  - `archive` — create, extract, and list zip archives
  - `network_diagnostics` — ping, DNS lookup, and HTTP connectivity checks
- **OpenOrca.Cli.Tests** — new test project with 84 unit tests covering CommandParser, ToolCallParser, ToolCallExecutor, ReplState, ConcurrencyTests, and ConsoleHelper
- **ILogger property injection** in tools — tools opt into logging via a public `Logger` property, injected by ToolRegistry at discovery time
- **Code coverage** collection and quality gates in CI
- **Security tests** — dedicated security validation test suite
- **Streaming idle timeout** — resettable timeout per streaming loop (default 120s, configurable via `StreamingTimeoutSeconds`)
- **Per-domain rate limiting** for web tools (1.5s minimum delay per domain)
- **Progress spinners** for long-running CLI operations
- **Configurable chars-per-token** estimation (`charsPerToken` in context config, default 3.5)
- **URL validation** in ConfigEditor before saving base URL
- **Input validation** in AgentOrchestrator

### Fixed
- **Shell injection prevention** — pass shell commands via stdin instead of argument escaping
- **Git argument sanitization** — prevent shell injection in all git tool arguments
- **Path traversal validation** — validate paths during recursive copy
- **Text-based tool results** in AgentOrchestrator for non-native models
- **Symlink resolution** before path safety checks
- **Thread safety** in PermissionManager and ConversationManager
- **CRLF line endings** handling in EditFileTool
- **Socket exhaustion** prevention with shared static HttpClient
- **Background process cleanup** on app exit and handle leak prevention
- **WriteFileTool** content rejection narrowed to known LLM role tags
- **Move overwrite** — delete existing destination directory before move
- **Web search** fallback regex patterns and parsing failure detection
- **Bare catch blocks** replaced with specific exception types
- **Static compiled Regex** instances in ToolCallParser and PromptManager for performance

### Changed
- **Hardcoded timeouts and limits** centralized into `CliConstants`
- **Ctrl+O thinking toggle** extracted into `CheckThinkingToggle()` method

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
