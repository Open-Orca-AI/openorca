# Troubleshooting

## Start with `/doctor`

The `/doctor` command (alias: `/diag`) checks your entire setup in one go. Run it first:

```
> /doctor
```

It tests: LM Studio connection, model configuration, tools, config file, log directory, session storage, prompt templates, project instructions, and native tool calling.

## Connection Issues

### "Unreachable" in `/doctor` or on startup

**Cause:** OpenOrca can't connect to the LLM server.

**Fix:**
1. Make sure LM Studio is running
2. Check the **Local Server** tab in LM Studio — it should say "Server started"
3. Verify the URL in your config matches LM Studio's server URL:
   ```
   > /config
   ```
   Default is `http://localhost:1234/v1`
4. Check if a firewall is blocking port 1234
5. If using a remote server, ensure the URL includes the full path (e.g., `http://192.168.1.100:1234/v1`)

### "No models loaded"

**Cause:** LM Studio is running but no model is loaded.

**Fix:** In LM Studio, load a model from the Home tab or My Models.

## Empty Responses

### LLM returns an empty response

**Cause:** Several possible reasons — model error, context overflow, or tool calling incompatibility.

**What OpenOrca does:** When an empty response is detected, it probes the LLM server with a raw HTTP request to get the actual error message (LM Studio sometimes returns errors as SSE events with HTTP 200, which the SDK silently drops).

**Fix:**
1. Check the error message shown in the terminal
2. Check logs at `~/.openorca/logs/` for details
3. If using `nativeToolCalling: true`, try setting it to `false` — some models return empty responses when they receive tool definitions
4. Try reducing context size — the conversation may have exceeded the model's window
5. Run `/compact` to shrink the conversation and try again

### Stream returns 0 tokens

**Cause:** The SDK streaming API may not surface content when the model returns only tool calls without text.

**What OpenOrca does:** Auto-detects this condition (updates received but 0 content items) and retries without native tool definitions. Sets `useNativeTools = false` for the rest of that agent loop.

**Fix:** Usually self-correcting. If it persists, set `nativeToolCalling: false` in config.

## Tool Calling Not Working

### Model describes what to do instead of calling tools

**Cause:** The model doesn't know how to use `<tool_call>` tags or native function calling.

**Fix:**
1. OpenOrca will automatically nudge the model to use tools (you'll see "Nudging model to use tool calls...")
2. If nudging fails, the model may not be capable enough for reliable tool use — try a different model
3. Check that the system prompt includes tool descriptions: load a new conversation with `/clear`
4. For models with native tool calling support, set `nativeToolCalling: true`

### Truncated tool calls

**Cause:** The model hit its token limit mid-generation, producing an incomplete `<tool_call>` tag.

**What OpenOrca does:** Detects unclosed `<tool_call>` tags and sends a continuation message asking the model to complete the tool call. Retries up to 2 times.

**Fix:** If this happens repeatedly, increase `maxTokens` in config or use a model with a larger output window.

### Tool call has empty arguments

**Cause:** SDK streaming limitation — when the model returns only `delta.tool_calls` without any text tokens, the SDK doesn't surface the arguments in individual streaming updates.

**What OpenOrca does:** Detects missing required arguments on native tool calls and auto-downgrades to text-based tool calling.

**Fix:** Usually self-correcting. Set `nativeToolCalling: false` if it keeps happening.

## Model-Specific Issues

### DeepSeek R1: Empty responses with native tools

DeepSeek R1 models return empty responses when OpenAI tool definitions are sent.

**Fix:** Set `nativeToolCalling: false` in config. This is required for all DeepSeek R1 variants.

### Mistral fine-tunes: Broken tool calling

Custom Unsloth fine-tunes may have broken Jinja chat templates.

**Fix:** In LM Studio: **My Models > (select model) > Settings > Prompt Template**. Verify the template is correct for Mistral Instruct format.

### Model wraps everything in `<think>` tags

This is normal behavior for DeepSeek R1 and similar reasoning models. OpenOrca strips `<think>...</think>` blocks and looks for tool calls in the remaining text.

**Fix:** No fix needed — this works automatically. Press `Ctrl+O` if you want to see the thinking output.

## Permission Issues

### Tool call stuck waiting for approval

**Cause:** The tool's risk level requires manual approval and you haven't configured auto-approve.

**Fix:** Either:
- Approve the tool call when prompted
- Set `autoApproveModerate: true` for Moderate-risk tools
- Add specific tools to `alwaysApprove`: `["write_file", "edit_file"]`
- Set `autoApproveAll: true` to approve everything (use with caution)

### Tool is disabled

**Cause:** The tool is listed in `disabledTools` in config.

**Fix:** Remove it from the `disabledTools` array in `~/.openorca/config.json`.

## Context Window Issues

### Model responses getting worse / confused

**Cause:** Conversation has grown beyond the effective context window. Even if you haven't hit the hard limit, models perform worse as context fills up.

**Fix:**
1. Run `/context` to check usage
2. Run `/compact` to summarize older messages
3. Use `/clear` and start fresh if the task has changed

### Auto-compaction keeps triggering

**Cause:** `contextWindowSize` is set too low, or `autoCompactThreshold` is too aggressive.

**Fix:**
1. Increase `contextWindowSize` if your model supports a larger window
2. Raise `autoCompactThreshold` (e.g., from 0.8 to 0.9)
3. Set `autoCompactEnabled: false` and compact manually

## Platform-Specific Issues

### Windows: `!` shell commands fail

The `!` shortcut runs commands via `cmd.exe /c`. Some Unix commands won't work.

**Fix:** Use Windows-native commands, or prefix with `bash -c "..."` if you have Git Bash or WSL.

### Linux: clipboard copy fails

The `/copy` command uses `xclip`. If it's not installed, the copy will fail.

**Fix:** Install xclip: `sudo apt-get install xclip` (Debian/Ubuntu) or `sudo dnf install xclip` (Fedora).

## Reporting Bugs

When filing an issue, please include:

1. **OpenOrca version** — `openorca --version`
2. **OS and version** — e.g., Windows 11, Ubuntu 24.04
3. **LM Studio version** and model being used
4. **Steps to reproduce** — what you typed and what happened
5. **Log output** — relevant lines from `~/.openorca/logs/openorca-{date}.log`
6. **`/doctor` output** — run `/doctor` and include the table

File issues at: [github.com/Open-Orca-AI/openorca/issues](https://github.com/Open-Orca-AI/openorca/issues)
