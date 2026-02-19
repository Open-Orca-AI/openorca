# Hooks & Extensibility

Hooks let you run custom shell commands before and after tool execution. They're configured in `~/.openorca/config.json` under the `hooks` section.

## Configuration

```json
{
  "hooks": {
    "preToolHooks": {
      "bash": "echo 'About to run bash' >> /tmp/orca-audit.log",
      "*": "echo 'Tool starting' >> /tmp/orca-audit.log"
    },
    "postToolHooks": {
      "write_file": "echo 'File written' >> /tmp/orca-audit.log",
      "*": "echo 'Tool finished' >> /tmp/orca-audit.log"
    }
  }
}
```

### Hook Keys

| Key | Matches |
|-----|---------|
| `"bash"` | Only the `bash` tool |
| `"write_file"` | Only the `write_file` tool |
| `"*"` | Any tool (wildcard) |

Specific tool names are checked first. If no specific match is found, the wildcard `*` is used.

## Behavior

### Pre-Tool Hooks

- Run **before** the tool executes
- **Blocking:** if the hook exits with a non-zero exit code, the tool execution is **cancelled**
- Use for safety gates, audit logging, or validation
- Timeout: 30 seconds

### Post-Tool Hooks

- Run **after** the tool executes (regardless of tool success/failure)
- **Fire-and-forget:** non-zero exit codes are logged but don't affect anything
- Use for audit logging, notifications, or cleanup
- Timeout: 30 seconds

## Environment Variables

Hooks receive context about the tool call via environment variables:

| Variable | Available In | Description |
|----------|-------------|-------------|
| `ORCA_TOOL_NAME` | Pre + Post | Name of the tool being executed (e.g., `bash`, `write_file`) |
| `ORCA_TOOL_ARGS` | Pre + Post | JSON string of the tool's arguments |
| `ORCA_TOOL_RESULT` | Post only | The tool's result text (truncated to 10,000 chars) |
| `ORCA_TOOL_ERROR` | Post only | `"True"` if the tool returned an error, `"False"` otherwise |

## Use Case Examples

### Audit Logging

Log every tool call to a file:

```json
{
  "hooks": {
    "preToolHooks": {
      "*": "echo \"$(date -Iseconds) PRE  $ORCA_TOOL_NAME $ORCA_TOOL_ARGS\" >> ~/.openorca/audit.log"
    },
    "postToolHooks": {
      "*": "echo \"$(date -Iseconds) POST $ORCA_TOOL_NAME error=$ORCA_TOOL_ERROR\" >> ~/.openorca/audit.log"
    }
  }
}
```

### Safety Gate: Block Dangerous Paths

Prevent writes to sensitive directories:

```json
{
  "hooks": {
    "preToolHooks": {
      "write_file": "python3 -c \"import json,sys,os; args=json.loads(os.environ['ORCA_TOOL_ARGS']); sys.exit(1 if '/etc/' in args.get('path','') or '/.ssh/' in args.get('path','') else 0)\""
    }
  }
}
```

If the path contains `/etc/` or `/.ssh/`, the Python script exits with code 1 and the write is blocked.

### Auto-Format After File Write

Run a formatter after any file write:

```json
{
  "hooks": {
    "postToolHooks": {
      "write_file": "python3 -c \"import json,os; args=json.loads(os.environ['ORCA_TOOL_ARGS']); path=args.get('path',''); os.system(f'prettier --write {path}') if path.endswith(('.js','.ts','.json')) else None\""
    }
  }
}
```

### Git Safety: Prevent Pushes to Main

Block `git_push` if targeting the main branch:

```json
{
  "hooks": {
    "preToolHooks": {
      "git_push": "python3 -c \"import json,os,sys; args=json.loads(os.environ['ORCA_TOOL_ARGS']); sys.exit(1 if args.get('branch','') in ('main','master') else 0)\""
    }
  }
}
```

### Notification on Shell Commands

Get a desktop notification when a shell command completes (macOS):

```json
{
  "hooks": {
    "postToolHooks": {
      "bash": "osascript -e 'display notification \"Command finished\" with title \"OpenOrca\"'"
    }
  }
}
```

## Shell Execution

Hooks run via:
- **Windows:** `cmd.exe /c <command>`
- **Linux/macOS:** `/bin/bash -c "<command>"`

The working directory is the current working directory of the OpenOrca process.

## Prompt Template Customization

Beyond hooks, you can customize the system prompt templates that control how OpenOrca instructs the model.

### Editing Prompt Templates

Prompt files are stored at `~/.openorca/prompts/`. You can edit them directly:

```bash
# Edit the default prompt
nano ~/.openorca/prompts/default.md

# Edit a model-specific prompt
nano ~/.openorca/prompts/mistral-7b-instruct-v0.3.md
```

### Template Variables

Templates support these placeholders:

| Variable | Replaced With |
|----------|--------------|
| `{{TOOL_LIST}}` | Formatted list of all enabled tools with descriptions |
| `{{CWD}}` | Current working directory |
| `{{PLATFORM}}` | Operating system identifier |
| `{{PROJECT_INSTRUCTIONS}}` | Contents of ORCA.md (if present) |

### Forcing a Specific Profile

Set `promptProfile` in config to override automatic model-based resolution:

```json
{
  "lmStudio": {
    "promptProfile": "my-custom-prompt"
  }
}
```

This loads `~/.openorca/prompts/my-custom-prompt.md` regardless of which model is loaded.
