# Model Setup

OpenOrca works with any model served through an OpenAI-compatible API. However, models vary in their tool calling ability. This guide helps you choose and configure the right model.

## Compatibility Table

| Model | Native Tool Calling | Text-Based Tool Calling | Notes |
|-------|:-------------------:|:----------------------:|-------|
| Mistral 7B Instruct v0.3 | Yes | Yes | Best tested. Recommended starting point. |
| DeepSeek R1 Distill Qwen 7B | No | Yes | Uses `<think>` tags + bare JSON. Set `nativeToolCalling: false`. |
| Llama 3 / 3.1 | Varies | Yes | Depends on quantization. Test with your specific version. |
| Qwen 2 / 2.5 | Varies | Yes | Generally good with text-based calling. |
| Any OpenAI-compatible | Varies | Yes | Auto-fallback to text-based if native fails. |

## Native vs Text-Based Tool Calling

OpenOrca supports two modes for invoking tools:

### Native Tool Calling (`nativeToolCalling: true`)
- Tool definitions are sent via the OpenAI function calling protocol
- The model returns structured `tool_calls` in its response
- Fastest and most reliable when the model supports it
- Set in config: `"nativeToolCalling": true`

### Text-Based Tool Calling (`nativeToolCalling: false`)
- Tools are described in the system prompt as text
- The model outputs tool calls using `<tool_call>` tags or bare JSON
- OpenOrca parses these from the model's text output
- Works with any model, even those without function calling support

### Auto-Downgrade

If `nativeToolCalling` is `true` but the model returns empty responses when tool definitions are sent, OpenOrca automatically retries without native tools and falls back to text-based mode for the rest of that agent loop. You'll see this in the logs.

## Per-Model Setup

### Mistral 7B Instruct v0.3

```json
{
  "lmStudio": {
    "model": "mistral-7b-instruct-v0.3",
    "nativeToolCalling": true,
    "temperature": 0.7
  }
}
```

**LM Studio tip:** If you're using a custom fine-tune (e.g., Unsloth), check that the Jinja chat template is correct. In LM Studio: **My Models > model settings > Prompt Template**. Broken templates cause tool calling to fail silently.

### DeepSeek R1 Distill Qwen 7B

```json
{
  "lmStudio": {
    "model": "deepseek-r1-distill-qwen-7b",
    "nativeToolCalling": false,
    "temperature": 0.7
  }
}
```

DeepSeek R1 models wrap all reasoning in `<think>...</think>` tags and output tool calls as bare JSON after the closing `</think>` tag. OpenOrca strips the thinking and parses the JSON automatically.

**Important:** This model returns empty responses when OpenAI tool definitions are sent. Always set `nativeToolCalling: false`.

### Llama 3 / 3.1

```json
{
  "lmStudio": {
    "model": "your-llama-model-id",
    "nativeToolCalling": false,
    "temperature": 0.7
  }
}
```

Start with `nativeToolCalling: false` and test. Some Llama quantizations support native calling — try `true` if text-based works first.

## Prompt Profiles

OpenOrca uses model-specific system prompt templates stored in `~/.openorca/prompts/`. The system auto-generates a prompt file based on the model name the first time you use a model.

### How Resolution Works

1. **Explicit override** — if `promptProfile` is set in config, uses `~/.openorca/prompts/{profileName}.md`
2. **Model-specific auto-generated** — looks for or creates `~/.openorca/prompts/{model-slug}.md`
3. **Default fallback** — uses `~/.openorca/prompts/default.md`

### Customizing Prompts

You can edit any `.md` file in `~/.openorca/prompts/` to customize the system prompt. Available template variables:

| Variable | Replaced With |
|----------|--------------|
| `{{TOOL_LIST}}` | Formatted list of all enabled tools |
| `{{CWD}}` | Current working directory |
| `{{PLATFORM}}` | Operating system identifier |
| `{{PROJECT_INSTRUCTIONS}}` | Contents of ORCA.md if present |

### Force a Specific Profile

```json
{
  "lmStudio": {
    "promptProfile": "default"
  }
}
```

Set to `null` (or omit) for automatic model-based resolution.

## Nudge Mechanism

When a model outputs code blocks that look like they should be tool calls but doesn't use `<tool_call>` tags, OpenOrca "nudges" the model by sending a follow-up message asking it to use the proper format. This gives the model a second chance without wasting the entire response.

Nudging triggers when:
- The response contains code blocks with tool-like JSON but no `<tool_call>` tags
- The response contains code blocks with action words (create, write, save) and file paths

## Diagnosing Model Issues

If tool calling isn't working:

1. Run `/doctor` to check connectivity and model status
2. Check the logs at `~/.openorca/logs/` for detailed request/response info
3. Try toggling `nativeToolCalling` between `true` and `false`
4. Ensure your model is loaded in LM Studio (check the Local Server tab)
5. For custom fine-tunes, verify the chat template in LM Studio model settings
6. Try a simpler prompt like "Read the file README.md" to test basic tool calling

## Supported Text Tool Call Formats

OpenOrca recognizes these patterns in model output:

| Format | Example |
|--------|---------|
| `<tool_call>` tags | `<tool_call>{"name": "read_file", "arguments": {"path": "README.md"}}</tool_call>` |
| `<\|tool_call\|>` tags | `<\|tool_call\|>{"name": "...", "arguments": {...}}<\|/tool_call\|>` |
| `[TOOL_CALL]` tags | `[TOOL_CALL]{"name": "...", "arguments": {...}}[/TOOL_CALL]` |
| `<function_call>` tags | `<function_call>{"name": "...", "arguments": {...}}</function_call>` |
| JSON in code fences | `` ```json\n{"name": "...", "arguments": {...}}\n``` `` |
| Bare JSON | `{"name": "read_file", "arguments": {"path": "README.md"}}` |
| Function wrapper | `{"function": {"name": "...", "arguments": {...}}}` |
