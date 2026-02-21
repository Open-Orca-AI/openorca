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
    public MemoryConfig Memory { get; set; } = new();
    public ThinkingConfig Thinking { get; set; } = new();
    public ShellConfig Shell { get; set; } = new();
    public Dictionary<string, McpServerConfig> McpServers { get; set; } = [];

    /// <summary>
    /// Runtime-only flag set by --demo CLI argument. Never serialized to config.json.
    /// </summary>
    [JsonIgnore]
    public bool DemoMode { get; set; }

    /// <summary>
    /// Runtime-only flag set by --sandbox or --simple CLI argument. Never serialized.
    /// </summary>
    [JsonIgnore]
    public bool SandboxMode { get; set; }

    /// <summary>
    /// Runtime-only directory restriction set by --allow-dir CLI argument. Never serialized.
    /// </summary>
    [JsonIgnore]
    public string? AllowedDirectory { get; set; }
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
    /// Maximum idle time (seconds) during streaming before aborting.
    /// If no tokens are received for this duration, the stream is cancelled.
    /// </summary>
    public int StreamingTimeoutSeconds { get; set; } = 120;

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

    /// <summary>
    /// Glob patterns that auto-allow specific tool+arg combinations.
    /// Format: "ToolName(argGlob)" — e.g. "Bash(git *)", "Write(src/**)".
    /// </summary>
    public List<string> AllowPatterns { get; set; } = [];

    /// <summary>
    /// Glob patterns that deny specific tool+arg combinations. Deny wins over allow.
    /// Format: "ToolName(argGlob)" — e.g. "Bash(rm -rf *)", "Bash(sudo *)".
    /// </summary>
    public List<string> DenyPatterns { get; set; } = [];
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

public sealed class MemoryConfig
{
    public bool AutoMemoryEnabled { get; set; } = true;
    public int MaxMemoryFiles { get; set; } = 20;
}

public sealed class ThinkingConfig
{
    /// <summary>
    /// Maximum tokens for &lt;think&gt; reasoning. 0 = unlimited.
    /// </summary>
    public int BudgetTokens { get; set; }

    /// <summary>
    /// Default visibility of thinking output on startup.
    /// </summary>
    public bool DefaultVisible { get; set; }
}

public sealed class ShellConfig
{
    /// <summary>
    /// Abort bash commands that produce no stdout for this many seconds.
    /// Suggests start_background_process instead. 0 = disabled. Default: 15.
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 15;
}

public sealed class McpServerConfig
{
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = [];
    public Dictionary<string, string> Env { get; set; } = [];
    public bool Enabled { get; set; } = true;
}
