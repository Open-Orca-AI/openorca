# Quick Start

This guide walks you through your first OpenOrca session.

## Start OpenOrca

Make sure LM Studio is running with a model loaded (see [Installation](Installation)), then:

```bash
openorca
```

You'll see a banner with connection status and the loaded model name. If it shows **Connected**, you're good.

## Your First Prompt

At the `>` prompt, ask OpenOrca to do something:

```
> Create a Python script that reads a CSV file and prints the top 5 rows
```

OpenOrca will:
1. **Plan** what to do (you'll see its thinking)
2. **Call tools** — create files, run shell commands, etc.
3. **Observe** the results of each tool call
4. **Iterate** — fix errors, refine, continue until done

Each tool call is shown with the tool name, arguments, and result. Depending on your [permission settings](Configuration), you may be asked to approve certain tools.

## The Agent Loop

When you send a message, OpenOrca enters an **agent loop** that runs up to 25 iterations:

1. Your message is sent to the LLM along with the full conversation context
2. The LLM streams a response, potentially including tool calls
3. Tool calls are executed and results are fed back to the LLM
4. Repeat until the LLM responds without tool calls (task complete)

If the model gets stuck in a retry loop (same tool failing repeatedly), OpenOrca auto-detects it and stops.

## Essential Commands

| Command | What It Does |
|---------|-------------|
| `/help` | Show all available commands |
| `/clear` | Clear conversation and start fresh |
| `/context` | Show how much of the context window is used |
| `/compact` | Summarize and compress the conversation to free context |
| `/doctor` | Run diagnostic checks |

See [Commands & Shortcuts](Commands-and-Shortcuts) for the full list.

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+O` | Toggle thinking output visibility (default: hidden) |
| `Ctrl+C` | First press: cancel current generation. Second press within 2s: exit |

When thinking is hidden, you'll see a token counter while the model generates. Press `Ctrl+O` to see the full streaming output.

## Single-Prompt Mode

For scripting or CI, pass a prompt directly and OpenOrca will run non-interactively:

```bash
openorca --prompt "List all .cs files in this project"
```

## Demo Mode

Run a demo without connecting to an LLM server:

```bash
openorca --demo
```

## Next Steps

- [Model Setup](Model-Setup) — configure your model for optimal tool calling
- [Tool Reference](Tool-Reference) — see all 31 available tools
- [Configuration](Configuration) — customize permissions, context window, and more
- [Project Instructions](Project-Instructions) — teach OpenOrca about your project with ORCA.md
