# Commands & Shortcuts

## Slash Commands

All commands start with `/` and can be typed at the `>` prompt.

### `/help`, `/h`, `/?`

Show available commands and current mode status.

### `/clear`, `/c`

Clear the conversation history and start fresh. Resets the system prompt and clears the terminal.

### `/model [name]`

Without arguments, lists all available models on the connected LM Studio server. With a model name, switches to that model.

```
> /model                              # List available models
> /model mistral-7b-instruct-v0.3     # Switch to a specific model
```

### `/config`

Opens an interactive configuration editor. Walks through each setting section and lets you modify values.

### `/session list|save [name]|load <id>|delete <id>`

Manage conversation sessions.

```
> /session list           # Show saved sessions (up to 20)
> /session save           # Save current session (auto-generated title)
> /session save My Task   # Save with a custom title
> /session load abc123    # Load a saved session by ID
> /session delete abc123  # Delete a saved session
```

Sessions are stored in `~/.openorca/sessions/`. See [Sessions & Context](Sessions-and-Context) for details.

### `/plan`, `/p [on|off]`

Toggle plan mode. In plan mode, the model plans before executing and asks for approval.

```
> /plan          # Toggle plan mode
> /plan on       # Enable plan mode
> /plan off      # Disable plan mode
```

### `/compact [instructions]`

Summarize the conversation to free up context window space. Optionally provide focus instructions.

```
> /compact                              # Compact with default summarization
> /compact focus on the auth changes    # Compact with specific focus
```

The compaction preserves the most recent turns (configurable via `compactPreserveLastN`). See [Sessions & Context](Sessions-and-Context).

### `/rewind [N]`

Remove the last N conversation turns. Defaults to 1 if no number is given.

```
> /rewind        # Remove last turn
> /rewind 3      # Remove last 3 turns
```

### `/context`, `/ctx`

Show context window usage: estimated tokens, window size, usage percentage, auto-compact threshold, and a visual progress bar. Also shows message counts by role.

### `/stats`, `/cost`

Show session statistics: duration, total turns, output tokens, messages in context, average tokens per turn.

### `/memory [show|edit|list|auto|clear-auto]`

View or edit project instructions and manage auto memory.

```
> /memory           # Show current ORCA.md contents
> /memory show      # Same as above
> /memory edit      # Open ORCA.md in your $EDITOR (default: notepad on Windows, nano on Linux/macOS)
> /memory list      # List all auto-generated memory files with previews
> /memory auto on   # Enable auto memory (saves learnings at session end)
> /memory auto off  # Disable auto memory
> /memory clear-auto # Delete all auto-generated memory files
```

See [Project Instructions](Project-Instructions) for details on ORCA.md and [Auto Memory](Auto-Memory) for the auto memory system.

### `/doctor`, `/diag`

Run diagnostic checks against your setup. Tests:

| Check | Description |
|-------|-------------|
| LM Studio connection | Verifies connectivity and lists loaded models |
| Model configured | Shows current model or "Auto-detect" |
| Tools registered | Shows count of registered tools |
| Config file | Checks if `~/.openorca/config.json` exists |
| Log directory | Verifies write access to `~/.openorca/logs/` |
| Session storage | Checks session directory accessibility |
| Default prompt template | Looks for `~/.openorca/prompts/default.md` |
| Project instructions | Checks for ORCA.md in the current project |
| Native tool calling | Shows current setting |

### `/copy`, `/cp`

Copy the last assistant response to the clipboard. Strips `<think>` tags before copying.

- Windows: uses `clip`
- macOS: uses `pbcopy`
- Linux: uses `xclip -selection clipboard`

### `/export [path]`

Export the full conversation to a markdown file.

```
> /export                             # Export to openorca-export-{timestamp}.md
> /export my-session.md               # Export to a specific file
```

The export includes system prompt (truncated), all messages by role, tool calls with arguments, and tool results.

### `/checkpoint [list|diff|restore|clear]`

Manage file checkpoints. OpenOrca automatically snapshots files before any file-modifying tool (edit_file, write_file, delete_file, copy_file, move_file) executes.

```
> /checkpoint list              # List all checkpointed files with timestamps
> /checkpoint diff src/main.cs  # Show diff between checkpoint and current file
> /checkpoint restore src/main.cs  # Restore file to its checkpointed state
> /checkpoint clear             # Delete all checkpoints for the current session
```

See [File Checkpoints](File-Checkpoints) for details.

### `/init`

Scaffold a new `.orca/ORCA.md` project instructions file with a starter template. Uses `ProjectInstructionsLoader.FindProjectRoot()` to locate the project root, then creates `.orca/ORCA.md` with sections for overview, architecture, code style, testing, and common commands.

If an ORCA.md already exists, it tells you and suggests `/memory edit` instead.

### `/diff`

Show uncommitted git changes in formatted Spectre.Console panels:

- **Staged changes** — shown in a green-bordered panel with stat summary + full diff
- **Unstaged changes** — shown in a yellow-bordered panel with stat summary + full diff

If there are no changes, prints "No uncommitted changes."

### `/undo`

Revert AI-made (or any uncommitted) changes with a confirmation step:

1. Shows a panel with `git diff --stat` of all staged + unstaged changes
2. Offers three choices via an interactive prompt:
   - **Revert all** — runs `git reset HEAD` + `git checkout .`
   - **Stash changes** — runs `git stash`
   - **Cancel** — does nothing

### `/rename <name>`

Rename the current session. Requires an active session (save first with `/session save` if needed).

```
> /rename Refactoring auth module
```

### `/add <file1> [file2] ...`

Add file contents to the conversation context so the LLM can reference them. Supports glob patterns.

```
> /add src/Program.cs                    # Add a single file
> /add src/*.cs                          # Add all .cs files in src/
> /add src/Models/User.cs tests/UserTests.cs  # Add multiple specific files
```

Each file is injected as a user message with a path header. Files are truncated at 50K characters.

### `/ask [question]`

Without arguments, toggles **Ask mode** — a persistent mode where all input is sent to the LLM without tools. The prompt shows `[ask] ❯` when active. Toggle off by running `/ask` again or pressing Shift+Tab.

With arguments, performs a **one-shot ask** — sends the question without tools and returns to the current mode.

```
> /ask                                                    # Toggle ask mode on/off
> /ask what is the difference between Task and ValueTask in C#?  # One-shot ask
> /ask explain the strategy pattern with an example              # One-shot ask
```

### `/exit`, `/quit`, `/q`

Exit OpenOrca.

## Custom Commands

You can define project-specific or global custom slash commands by creating `.md` files in command directories.

### Command Directories

| Location | Scope |
|----------|-------|
| `.orca/commands/` | Project-level (checked first) |
| `~/.openorca/commands/` | Global (used as fallback) |

### Creating a Custom Command

Create a markdown file named after the command. For example, `.orca/commands/review-pr.md`:

```markdown
Review PR #{{ARG1}} thoroughly. Check for:
- Code style consistency
- Security issues
- Test coverage
{{ARGS}}
```

### Template Variables

| Variable | Description |
|----------|-------------|
| `{{ARGS}}` | All arguments joined with spaces |
| `{{ARG1}}`, `{{ARG2}}`, ... | Positional arguments |

### Usage

```
> /review-pr 123                # Runs the review-pr.md template with ARG1=123
> /deploy staging --verbose     # Runs deploy.md with ARGS="staging --verbose"
```

Custom commands inject the expanded template as a user message and run it through the agent loop. Built-in commands always take priority over custom commands with the same name.

See [Custom Commands](Custom-Commands) for more details.

## Shell Shortcut

Prefix any command with `!` to run it directly in the shell, bypassing the LLM:

```
> !git status
> !ls -la
> !python script.py
```

Output is displayed in a panel with exit code. Commands time out after 120 seconds. Output is truncated at 5000 characters.

On Windows, commands run via `cmd.exe /c`. On Linux/macOS, via `/bin/bash -c`.

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Shift+Tab` | Cycle input mode: **Normal** → **Plan** → **Ask** → **Normal**. The prompt indicator updates immediately (`❯`, `[plan] ❯`, `[ask] ❯`). |
| `Ctrl+O` | Toggle thinking visibility. When hidden, you see a token counter. When visible, you see the full streaming output in cyan. Can be toggled mid-generation. |
| `Ctrl+C` | **First press:** Cancel the current generation. **Second press within 2 seconds:** Exit the application. |
| `Escape` | Clear the current input line. |

## Input Modes

OpenOrca has three input modes, cycled with **Shift+Tab**:

| Mode | Prompt | Behavior |
|------|--------|----------|
| **Normal** | `❯` | Full agent loop with all tools available |
| **Plan** | `[plan] ❯` | Model plans without executing, then asks for approval |
| **Ask** | `[ask] ❯` | Chat without tools — faster, cheaper responses |

You can also switch modes with commands:
- `/plan` or `/plan on|off` — toggle or set Plan mode
- `/ask` (no args) — toggle Ask mode

## Multi-Line Input

End a line with `\` to continue on the next line. In non-interactive mode (piped input), `Console.ReadLine()` is used as a fallback.
