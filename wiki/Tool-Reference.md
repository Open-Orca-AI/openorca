# Tool Reference

OpenOrca includes 31 built-in tools organized by category. Each tool has a **risk level** that determines whether it requires user approval.

## Permission Levels

| Level | Behavior | Examples |
|-------|----------|---------|
| **ReadOnly** | Auto-approved by default | Reading files, searching, thinking |
| **Moderate** | Requires approval (unless `autoApproveModerate` is on) | Writing files, git commit, mkdir |
| **Dangerous** | Always requires approval (unless `autoApproveAll` is on) | Shell execution, git push |

You can customize permissions in [Configuration](Configuration). Specific tools can be added to `alwaysApprove` or `disabledTools`.

---

## File System (11 tools)

### `read_file`
**Risk:** ReadOnly

Read file contents with line numbers.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | File path to read |
| `offset` | integer | No | Line number to start from |
| `limit` | integer | No | Max lines to read |

### `write_file`
**Risk:** Moderate

Create or overwrite a file.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | File path to write |
| `content` | string | Yes | File content |

### `edit_file`
**Risk:** Moderate

Replace an exact string in a file. The `old_string` must match exactly (including whitespace).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | File path |
| `old_string` | string | Yes | Text to find |
| `new_string` | string | Yes | Replacement text |

### `delete_file`
**Risk:** Moderate

Delete a file or directory.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Path to delete |

### `copy_file`
**Risk:** Moderate

Copy a file or directory.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `source` | string | Yes | Source path |
| `destination` | string | Yes | Destination path |

### `move_file`
**Risk:** Moderate

Move or rename a file.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `source` | string | Yes | Source path |
| `destination` | string | Yes | Destination path |

### `mkdir`
**Risk:** Moderate

Create a directory (and parent directories).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Directory path |

### `cd`
**Risk:** Moderate

Change the current working directory.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Directory to change to |

### `glob`
**Risk:** ReadOnly

Find files matching a glob pattern.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `pattern` | string | Yes | Glob pattern (e.g., `**/*.cs`) |
| `path` | string | No | Base directory (defaults to cwd) |

### `grep`
**Risk:** ReadOnly

Search file contents using regex.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `pattern` | string | Yes | Regex pattern |
| `path` | string | No | File or directory to search |
| `include` | string | No | Glob filter for files |

### `list_directory`
**Risk:** ReadOnly

List directory contents.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | No | Directory path (defaults to cwd) |

---

## Shell (4 tools)

### `bash`
**Risk:** Dangerous

Execute a shell command. Commands run via `cmd.exe` on Windows or `/bin/bash` on Linux/macOS.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `command` | string | Yes | Shell command to execute |
| `timeout` | integer | No | Timeout in seconds (default: 120) |

### `start_background_process`
**Risk:** Dangerous

Start a long-running process in the background. Returns a process ID for later use.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `command` | string | Yes | Command to run |

### `get_process_output`
**Risk:** ReadOnly

Read output from a background process.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `pid` | string | Yes | Process ID |

### `stop_process`
**Risk:** Moderate

Stop a running background process.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `pid` | string | Yes | Process ID |

---

## Git (9 tools)

### `git_status`
**Risk:** ReadOnly

Show working tree status.

### `git_diff`
**Risk:** ReadOnly

Show changes in the working tree.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `staged` | boolean | No | Show only staged changes |
| `path` | string | No | Limit diff to a specific path |

### `git_log`
**Risk:** ReadOnly

Show commit history.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `count` | integer | No | Number of commits to show |
| `oneline` | boolean | No | One-line format |

### `git_commit`
**Risk:** Moderate

Stage files and create a commit.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `message` | string | Yes | Commit message |
| `files` | string[] | No | Files to stage (defaults to all changes) |

### `git_branch`
**Risk:** Moderate

List, create, or delete branches.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `name` | string | No | Branch name to create/delete |
| `delete` | boolean | No | Delete the branch |

### `git_checkout`
**Risk:** Moderate

Switch branches or restore files.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `branch` | string | Yes | Branch name |
| `create` | boolean | No | Create branch if it doesn't exist |

### `git_push`
**Risk:** Dangerous

Push commits to a remote.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `remote` | string | No | Remote name (default: origin) |
| `branch` | string | No | Branch to push |

### `git_pull`
**Risk:** Moderate

Pull changes from a remote.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `remote` | string | No | Remote name (default: origin) |
| `branch` | string | No | Branch to pull |

### `git_stash`
**Risk:** Moderate

Stash or restore working directory changes.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `action` | string | No | `push`, `pop`, `list`, or `drop` |

---

## Web & GitHub (3 tools)

### `web_fetch`
**Risk:** ReadOnly

Fetch content from a URL. Returns the page content as text.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `url` | string | Yes | URL to fetch |

### `web_search`
**Risk:** ReadOnly

Search the web using DuckDuckGo.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `query` | string | Yes | Search query |
| `count` | integer | No | Number of results |

### `github`
**Risk:** Moderate

Run GitHub CLI (`gh`) commands. Requires `gh` to be installed and authenticated.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `command` | string | Yes | gh command to run (e.g., `pr list`) |

---

## Utility & Interactive (4 tools)

### `think`
**Risk:** ReadOnly

A scratchpad for the model to do step-by-step reasoning without taking action. Output is not visible to the user.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `thought` | string | Yes | Reasoning content |

### `task_list`
**Risk:** ReadOnly

Track progress on multi-step tasks. The model uses this to maintain a checklist of what's been done and what's left.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `action` | string | Yes | `add`, `complete`, `list`, or `clear` |
| `task` | string | No | Task description (for `add`/`complete`) |

### `ask_user`
**Risk:** ReadOnly

Ask the user a question and wait for their response. Used when the model needs clarification.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `question` | string | Yes | Question to ask |

### `spawn_agent`
**Risk:** Moderate

Launch an independent sub-agent to handle a focused task. The sub-agent has its own conversation context and tool access.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `task` | string | Yes | Task description for the sub-agent |
| `tools` | string[] | No | Specific tools to grant (defaults to all) |
