# File Checkpoints

OpenOrca automatically creates file checkpoints before any file-modifying tool executes. This gives you a safety net to review and revert AI-made changes.

## How It Works

When any of these tools run, OpenOrca snapshots the original file before the modification:

| Tool | What's checkpointed |
|------|-------------------|
| `edit_file` | The file being edited |
| `write_file` | The file being written (if it exists) |
| `delete_file` | The file being deleted |
| `copy_file` | The destination path (if it exists) |
| `move_file` | Both source and destination paths |
| `multi_edit` | All files being edited |

Checkpoints are stored per-session in `~/.openorca/checkpoints/{sessionId}/` with a JSON manifest tracking all snapshots.

## Commands

### `/checkpoint list`

Show all checkpointed files for the current session with timestamps.

```
> /checkpoint list
```

Displays a table with file path, timestamp, and file size for each checkpoint.

### `/checkpoint diff <file>`

Show the differences between the checkpointed (original) version and the current file.

```
> /checkpoint diff src/Program.cs
```

Outputs a unified diff showing what changed. Lines prefixed with `-` were in the original, lines with `+` are in the current version.

### `/checkpoint restore <file>`

Restore a file to its checkpointed (original) state.

```
> /checkpoint restore src/Program.cs
```

This copies the checkpoint back to the original file path, effectively undoing all changes made during the session.

### `/checkpoint clear`

Delete all checkpoints for the current session.

```
> /checkpoint clear
```

## Storage

Checkpoints are stored at:

```
~/.openorca/checkpoints/
└── {sessionId}/
    ├── manifest.json           # Maps file paths to backup files
    └── {timestamp}_{hash}.bak  # Original file content
```

The manifest is a JSON array of entries:

```json
[
  {
    "path": "src/Program.cs",
    "backupFile": "20260221120000_a1b2c3.bak",
    "timestamp": "2026-02-21T12:00:00Z",
    "sizeBytes": 4096
  }
]
```

## Notes

- Each file is only checkpointed **once per session** — the first snapshot is preserved even if the file is modified multiple times
- Files that don't exist yet (new file creation) are not checkpointed
- Checkpoints persist across session restores — if you resume a session, previous checkpoints are still available
- Use `/checkpoint clear` or start a new session to clean up checkpoints
