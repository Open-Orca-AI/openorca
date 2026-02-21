using System.Text.Json;
using System.Text.Json.Serialization;
using OpenOrca.Core.Configuration;
using OpenOrca.Core.Session;

namespace OpenOrca.Core.Serialization;

/// <summary>
/// Source-generated JSON context for persistence (sessions, config).
/// CamelCase + WriteIndented + IgnoreNull to match existing config/session format.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(SessionData))]
[JsonSerializable(typeof(OrcaConfig))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(IDictionary<string, object?>))]
[JsonSerializable(typeof(JsonElement))]
public partial class OrcaJsonContext : JsonSerializerContext;
