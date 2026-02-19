# Sessions & Context

OpenOrca manages conversation history through sessions and monitors context window usage to prevent exceeding your model's limits.

## Sessions

### Auto-Save

By default (`autoSave: true`), OpenOrca saves your conversation when you exit. The session is stored as a JSON file in `~/.openorca/sessions/`.

### Managing Sessions

```
> /session list                # List saved sessions (shows up to 20)
> /session save                # Save current session
> /session save My Refactor    # Save with a custom title
> /session load abc123         # Load a session by ID
> /session delete abc123       # Delete a session
```

The session list shows: ID, title, last updated timestamp, and message count.

### Session Storage

Sessions are JSON files at `~/.openorca/sessions/{id}.json`. Each contains:
- Session ID and title
- Creation and update timestamps
- Full message history (roles, text content, tool calls, tool results)

The `maxSessions` config setting (default: 100) limits how many sessions are kept. Oldest sessions are pruned when the limit is exceeded.

## Context Window

### Understanding Context

The **context window** is the total number of tokens your model can process at once — this includes the system prompt, all conversation history, and the next response. When you exceed it, the model starts losing information or producing errors.

### Checking Usage

Use `/context` (or `/ctx`) to see current context window usage:

- **Estimated tokens** — approximate token count of the full conversation
- **Context window** — your configured window size (from `contextWindowSize`)
- **Usage percentage** — color-coded: green (<70%), yellow (70-90%), red (>90%)
- **Auto-compact threshold** — when auto-compaction kicks in
- **Message counts** — breakdown by role (system, user, assistant, tool)
- **Visual bar** — `[#####-----]` showing usage at a glance

### Setting the Right Window Size

Set `contextWindowSize` in your config to match your model's actual context window:

| Model | Typical Context Window |
|-------|----------------------|
| Mistral 7B | 8192 or 32768 |
| Llama 3 8B | 8192 |
| DeepSeek R1 7B | 16384 or 32768 |
| Qwen 2.5 7B | 32768 |

Check your model's card in LM Studio for the exact value.

## Compaction

When conversations grow long, compaction summarizes older messages to free up context space.

### Manual Compaction

```
> /compact                              # Compact with default summarization
> /compact focus on the database schema  # Direct the summary to preserve specific context
```

What happens:
1. Messages older than the last N turns are collected (N = `compactPreserveLastN`, default 4)
2. These messages are sent to the LLM with a summarization prompt
3. The summary replaces the older messages
4. Recent turns are preserved exactly as-is
5. You see a before/after count: `Compacted: 24 messages -> 6 messages (~3200 -> ~800 tokens)`

### Auto-Compaction

When `autoCompactEnabled` is `true` (default), OpenOrca checks context usage at the start of each agent loop iteration. If usage exceeds `autoCompactThreshold` (default: 80%) of `contextWindowSize`, it automatically runs compaction.

You'll see `Compacting conversation...` in the output when this happens.

### Compaction Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `autoCompactEnabled` | `true` | Enable/disable auto-compaction |
| `autoCompactThreshold` | `0.8` | Trigger at this percentage of context window |
| `compactPreserveLastN` | `4` | Recent turns to keep verbatim |

## Rewind

Remove recent conversation turns without compacting:

```
> /rewind        # Remove the last turn (1 user message + 1 assistant response)
> /rewind 3      # Remove the last 3 turns
```

This is useful when the model went down a wrong path and you want to try a different approach.

## Best Practices

1. **Set the right context window** — match `contextWindowSize` to your model's actual limit
2. **Use `/context` regularly** — especially during long sessions
3. **Compact with focus instructions** — tell it what to preserve when context is precious
4. **Use `/rewind` for mistakes** — cheaper than compacting, keeps more context
5. **Start fresh for new tasks** — use `/clear` when switching to an unrelated task
6. **Save before long sessions** — use `/session save` as a checkpoint you can return to
