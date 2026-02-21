# Auto Memory

OpenOrca can automatically learn and remember project-specific patterns, conventions, and insights across sessions. At the end of each session, the LLM summarizes what it learned and saves it for future reference.

## How It Works

1. **During a session:** You interact with OpenOrca normally, using tools to read/write code, run commands, etc.
2. **At session end:** If auto memory is enabled and the session involved meaningful tool usage, OpenOrca asks the LLM: *"What project-specific patterns, conventions, or learnings should be remembered?"*
3. **Save:** The LLM's summary is saved as a `.md` file in the memory directory
4. **Next session:** All memory files are loaded into the system prompt, giving the LLM context about your project from previous sessions

## Memory Directories

| Location | Scope | Priority |
|----------|-------|----------|
| `.orca/memory/` | Project-level | Loaded first |
| `~/.openorca/memory/` | Global | Loaded second |

Project memory is loaded before global memory. When saving, learnings go to the project memory directory if a project root is found, otherwise to the global directory.

## Configuration

In `~/.openorca/config.json`:

```json
{
  "memory": {
    "autoMemoryEnabled": true,
    "maxMemoryFiles": 20
  }
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `autoMemoryEnabled` | bool | `true` | Save session learnings automatically at session end |
| `maxMemoryFiles` | int | `20` | Maximum memory files per directory. Oldest files are pruned when exceeded. |

## Commands

### `/memory list`

Show all auto-generated memory files with first-line previews.

```
> /memory list
```

### `/memory auto on|off`

Toggle auto memory on or off for the current configuration.

```
> /memory auto on    # Enable auto memory
> /memory auto off   # Disable auto memory
```

### `/memory clear-auto`

Delete all auto-generated memory files from both project and global directories.

```
> /memory clear-auto
```

### `/memory show` / `/memory edit`

These existing commands still work for viewing and editing the ORCA.md project instructions file (separate from auto memory).

## File Format

Memory files are named `{date}-{hash}.md` (e.g., `20260221-a1b2c3.md`). The hash is derived from the content to avoid duplicates.

Example memory file content:

```markdown
- Project uses file-scoped namespaces and sealed classes by default
- Tests are in xUnit with IDisposable temp directories for isolation
- Config is loaded from ~/.openorca/config.json via ConfigManager
- Tools implement IOrcaTool and are auto-discovered by ToolRegistry
```

## System Prompt Integration

Memory content is injected into the system prompt under a `PROJECT MEMORY (auto-learned):` section, after the project instructions (ORCA.md). This gives the LLM persistent context about your project without you having to repeat information.

## Tips

- Auto memory works best with consistent project usage over multiple sessions
- Project-level memory (`.orca/memory/`) can be added to `.gitignore` or committed for team sharing
- Use `/memory clear-auto` if memory becomes stale or incorrect
- Global memory captures patterns useful across all projects (e.g., your preferred coding style)
- The `maxMemoryFiles` limit prevents unbounded growth — oldest files are automatically pruned
