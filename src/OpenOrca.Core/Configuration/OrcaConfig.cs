using System.Text.Json.Serialization;

namespace OpenOrca.Core.Configuration;

public sealed class OrcaConfig
{
    public LmStudioConfig LmStudio { get; set; } = new();
    public PermissionsConfig Permissions { get; set; } = new();
    public SessionConfig Session { get; set; } = new();
    public ContextConfig Context { get; set; } = new();
    public AgentConfig Agent { get; set; } = new();
    public HooksConfig Hooks { get; set; } = new();

    /// <summary>
    /// Runtime-only flag set by --demo CLI argument. Never serialized to config.json.
    /// </summary>
    [JsonIgnore]
    public bool DemoMode { get; set; }
}

public sealed class LmStudioConfig
{
    public string BaseUrl { get; set; } = "http://localhost:1234/v1";
    public string ApiKey { get; set; } = "lm-studio";
    public string? Model { get; set; }
    public float Temperature { get; set; } = 0.7f;
    public int? MaxTokens { get; set; }
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Send tool definitions via the OpenAI function calling protocol.
    /// Default is on. Turn off if your model returns empty responses when tools are sent.
    /// Tools are also described in the system prompt as a fallback for text-based tool calling.
    /// </summary>
    public bool NativeToolCalling { get; set; } = false;

    /// <summary>
    /// Override which prompt profile (.md file) to use. If null, auto-resolves from model name.
    /// e.g. "default" uses ~/.openorca/prompts/default.md regardless of model.
    /// </summary>
    public string? PromptProfile { get; set; }
}

public sealed class PermissionsConfig
{
    public bool AutoApproveAll { get; set; }
    public bool AutoApproveReadOnly { get; set; } = true;
    public bool AutoApproveModerate { get; set; }
    public List<string> AlwaysApprove { get; set; } = [];
    public List<string> DisabledTools { get; set; } = [];
}

public sealed class SessionConfig
{
    public bool AutoSave { get; set; } = true;
    public int MaxSessions { get; set; } = 100;
}

public sealed class ContextConfig
{
    public int ContextWindowSize { get; set; } = 8192;
    public float AutoCompactThreshold { get; set; } = 0.8f;
    public int CompactPreserveLastN { get; set; } = 4;
    public bool AutoCompactEnabled { get; set; } = true;

    /// <summary>
    /// Average characters per token for estimation. Lower values are more conservative.
    /// Typical ranges: 3.0-3.5 for code-heavy content, 3.5-4.0 for English prose.
    /// </summary>
    public float CharsPerToken { get; set; } = 3.5f;
}

public sealed class AgentConfig
{
    public int MaxIterations { get; set; } = 15;
    public int TimeoutSeconds { get; set; } = 300;
}

public sealed class HooksConfig
{
    public Dictionary<string, string> PreToolHooks { get; set; } = new();
    public Dictionary<string, string> PostToolHooks { get; set; } = new();
}
