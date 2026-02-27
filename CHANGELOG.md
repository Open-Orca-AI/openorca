# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.12.1] - 2026-02-27

### Changed
- **Version sync fix** — aligned HttpHelper User-Agent version with assembly version (was stuck at 0.6.0)
- **Documentation updates** — updated tool count to 39 across README, website, and wiki; backfilled CHANGELOG for versions 0.7.0–0.12.0

## [0.12.0] - 2026-02-27

### Added
- **Docusaurus website** — full documentation site deployed to GitHub Pages at [openorca.uk](https://openorca.uk) with ocean theme
- **Dependabot** — automated dependency updates with auto-merge for patch versions
- **Custom domain** — configured site for openorca.uk

### Changed
- **CI: Dependabot auto-merge** — uses metadata check instead of PR approval
- **Dependabot version bumping** — creates PRs instead of pushing directly to main
- **Updated dependencies** — Markdig 0.45→1.0.0, actions/setup-dotnet 4→5, actions/cache 4→5, actions/upload-artifact 4→7, actions/download-artifact 4→8, Andrew-Chen-Wang/github-wiki-action 4→5

## [0.11.0] - 2026-02-26

### Added
- **Claude-style colored diff rendering** — edit_file tool shows diffs with line numbers before markers

### Fixed
- **edit_file: whitespace-normalized fallback** — matches old_string even when whitespace differs slightly
- **edit_file: double-escaped newlines** — handles `\\n` literals from local models
- **edit_file: error includes file content** — old_string-not-found errors now show the file for context
- **Diff rendering** — addition lines and surrounding context always visible
- **Checkpoint file lock race** — prevents deadlock when parallel tool calls snapshot concurrently
- **ask_user: stringified JSON arrays** — handles options passed as JSON string instead of array
- **ask_user: progress indicator overlap** — fixed Ctrl+C not cancelling and spinner overlap
- **ask_user: execution timeout exemption** — interactive prompts no longer time out
- **spawn_agent: parameter alias coverage** — expanded aliases and added smart inference

## [0.10.0] - 2026-02-25

### Added
- **Next-prompt ghost text suggestions** — context-aware prompt suggestions appear as grey ghost text, accept with Tab

## [0.9.0] - 2026-02-25

### Added
- **Lenient JSON type coercion** — tool parameters automatically coerce between strings, numbers, booleans, and arrays to handle local model quirks

## [0.8.1] - 2026-02-24

### Fixed
- **Panel redraw during streaming** — resolved flickering when Spectre panels update mid-stream
- **Interactive `/model`** — model selection now works interactively in the REPL

## [0.8.0] - 2026-02-24

### Added
- **4 new tools** (35 → 39 total):
  - `print` — always-visible user messages regardless of verbosity level
  - `create_directory` — create directories with automatic parent creation
  - `rename_file` — rename or move files with overwrite option
  - `list_processes` — show all background processes and their status
- **Streaming tool output** — `IStreamingOrcaTool` interface for real-time tool output during execution
- **Verbosity system** — 5-level verbosity (Ctrl+O cycles 0→4): quiet, tool calls, full output, thinking preview, full thinking
- **`/benchmark` enhancements** — `count=N` and `models=all|name1,name2` arguments; verbosity level shown in info bar

### Changed
- **BashTool refactored** — background execution support with enhanced lifecycle tracking
- **ToolCallExecutor** — streaming-aware execution pipeline
- **ThinkTagFilter** — MaxThinkingLines for thinking preview at verbosity level 3

### Fixed
- **FileLoggerProvider** — handles locked log files with suffix fallback

## [0.7.1] - 2026-02-24

### Fixed
- **Auto-correct LLM parameter errors** — automatically retries with corrected parameters when the model makes schema errors during streaming
- **Multiple JSON tool calls** — handles multiple tool call JSON objects in a single `<tool_call>` block

## [0.7.0] - 2026-02-23

### Added
- **Context7 integration** — automatic library documentation lookup for up-to-date API references

### Changed
- **Codebase cleanup** — bug fixes, directory rename, test consolidation
- **Docs sync** — all documentation, wiki, and CI workflows updated to reflect .NET 10 / v0.6.0

## [0.6.0] - 2026-02-23

### Changed
- **Upgraded to .NET 10** — TFM `net9.0` → `net10.0`, SDK 10.0, all CI/CD workflows updated
- **Migrated to xUnit v3** — `xunit` 2.9.3 → `xunit.v3` 3.2.2, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.0.1
- **Updated NuGet packages** — Markdig 0.37→0.45, Microsoft.Extensions.Hosting 9.0→10.0.3, Microsoft.Extensions.AI 9.5→10.3, Microsoft.Extensions.Options/Logging.Abstractions/FileSystemGlobbing 9.0.5→10.0.3
- **C# 14 `System.Threading.Lock`** — replaced `object` locks with dedicated `Lock` type in `FileLoggerProvider`, `ConversationManager`, `BackgroundProcessManager`, `TaskStore`
- **C# modernization** — simplified null-check-then-assign patterns to `??=` for `ModelId` assignment
- **SelfContained build fix** — made `<SelfContained>` conditional on `RuntimeIdentifier` to fix NETSDK1151 with test project references

## [0.5.0] - 2026-02-22

### Added
- **`--continue` / `-c` CLI flag** — resume the most recent saved session on startup
- **`--resume` / `-r <id>` CLI flag** — resume a specific session by ID on startup
- **`--allow <tools>` CLI flag** — pre-approve tools (comma-separated) for non-interactive `--prompt` mode, enabling CI/CD pipelines
- **`--output json` CLI flag** — output structured JSON (`{"response":"...","tokens":N}`) when combined with `--prompt`
- **`/init` command** — scaffold `.orca/ORCA.md` project instructions with a starter template
- **`/diff` command** — show staged and unstaged git changes in color-coded Spectre panels
- **`/undo` command** — revert or stash uncommitted changes with an interactive prompt (revert all / stash / cancel)
- **`/rename <name>` command** — rename the current session
- **`/add <file> [...]` command** — add file contents to conversation context (supports glob patterns)
- **`/ask [question]` command** — toggle persistent Ask mode (no args) or one-shot ask (with args)
- **Shift+Tab mode cycling** — cycle input mode at the prompt: Normal → Plan → Ask → Normal, with visual indicator (`❯`, `[plan] ❯`, `[ask] ❯`)
- **Interactive key-by-key input** — InputHandler rewritten with `Console.ReadKey` loop supporting Shift+Tab, Escape to clear, and non-interactive fallback for piped input

## [0.4.0] - 2026-02-21

### Added
- **`multi_edit` tool** (34 → 35 total tools) — batch edit multiple files atomically with automatic rollback on failure. Validates all edits before applying, and restores original files if any edit fails during write.
- **Permission glob patterns** — `allowPatterns` and `denyPatterns` in permissions config for fine-grained tool control. Patterns use `ToolName(argGlob)` syntax (e.g., `Bash(git *)`, `write_file(src/**)`). Deny patterns take priority over allow patterns.
- **File checkpoints** — automatic file snapshots before any file-modifying tool (edit_file, write_file, delete_file, copy_file, move_file). New `/checkpoint` command with `list`, `diff`, `restore`, and `clear` subcommands to manage checkpoints.
- **Custom slash commands** — create `.md` files in `.orca/commands/` (project) or `~/.openorca/commands/` (global) to define custom slash commands. Template substitution with `{{ARGS}}`, `{{ARG1}}`, `{{ARG2}}`, etc.
- **Auto memory** — session learnings are automatically saved to `~/.openorca/memory/` (or `.orca/memory/`) at session end and loaded into the system prompt for future sessions. Configurable via `memory.autoMemoryEnabled` and `memory.maxMemoryFiles`.
- **`/checkpoint` command** — `list` shows checkpointed files, `diff <file>` shows changes since checkpoint, `restore <file>` restores original, `clear` removes all checkpoints.
- **`/memory` extended** — new subcommands: `auto on|off` (toggle auto memory), `list` (show memory files), `clear-auto` (delete auto-generated memory files).
- **`PermissionPattern`** record — parses and matches `ToolName(argGlob)` permission patterns with case-insensitive tool name matching and wildcard argument matching.
- **`CheckpointManager`** — manages file snapshots with JSON manifest, supports snapshot, list, diff, restore, and cleanup operations.
- **`CustomCommandLoader`** — discovers and loads custom command templates from project and global directories.
- **`MemoryManager`** — manages auto-learned memory files with project-level and global storage, pruning, and listing.

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
