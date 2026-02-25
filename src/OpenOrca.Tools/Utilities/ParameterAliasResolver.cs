using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenOrca.Tools.Utilities;

/// <summary>
/// Resolves common parameter name aliases (e.g., file_path → path) before tool
/// validation and execution. Schema-aware: an alias only remaps if the canonical
/// name exists in the tool's ParameterSchema and the alias does NOT.
/// </summary>
public static class ParameterAliasResolver
{
    private static readonly Dictionary<string, string> AliasMap = new(StringComparer.Ordinal)
    {
        // path aliases
        ["file_path"] = "path",
        ["filepath"] = "path",
        ["file"] = "path",
        ["directory"] = "path",
        ["dir"] = "path",
        ["folder"] = "path",

        // command aliases
        ["cmd"] = "command",
        ["shell_command"] = "command",

        // task aliases
        ["instructions"] = "task",
        ["prompt"] = "task",
        ["objective"] = "task",
        ["goal"] = "task",
        ["description"] = "task",
        ["purpose"] = "task",

        // pattern aliases
        ["search"] = "pattern",
        ["query"] = "pattern",
        ["regex"] = "pattern",

        // edit string aliases
        ["find"] = "old_string",
        ["search_string"] = "old_string",
        ["replace"] = "new_string",
        ["replacement"] = "new_string",
    };

    /// <summary>
    /// Resolve known parameter aliases in the given args JSON, using the tool's
    /// parameter schema to determine canonical names. Returns the original JSON
    /// unchanged if no remapping is needed (zero-allocation common path).
    /// </summary>
    public static string ResolveAliases(string argsJson, JsonElement schema, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(argsJson) || argsJson == "{}")
            return argsJson;

        // Get canonical property names from schema
        if (!schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
            return argsJson;

        JsonDocument? doc;
        try
        {
            doc = JsonDocument.Parse(argsJson);
        }
        catch
        {
            return argsJson; /* malformed JSON — return original */
        }

        using (doc)
        {
            var root = doc.RootElement;

            // First pass: check if any remapping is needed
            bool needsRemap = false;
            foreach (var prop in root.EnumerateObject())
            {
                if (ShouldRemap(prop.Name, properties, root))
                {
                    needsRemap = true;
                    break;
                }
            }

            if (!needsRemap)
                return argsJson;

            // Second pass: rewrite with aliases resolved
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (ShouldRemap(prop.Name, properties, root))
                    {
                        var canonical = AliasMap[prop.Name];
                        logger?.LogDebug("Resolved parameter alias: {Alias} → {Canonical}", prop.Name, canonical);
                        writer.WritePropertyName(canonical);
                        prop.Value.WriteTo(writer);
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    /// <summary>
    /// Infers missing required string parameters from unrecognized string arguments.
    /// If exactly one required string arg is missing and exactly one unrecognized
    /// string arg is present, remaps the unrecognized arg to the missing required name.
    /// Returns original JSON unchanged if inference is ambiguous or not applicable.
    /// </summary>
    public static string InferMissingRequired(string argsJson, JsonElement schema, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(argsJson) || argsJson == "{}")
            return argsJson;

        if (!schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
            return argsJson;

        if (!schema.TryGetProperty("required", out var requiredArr) ||
            requiredArr.ValueKind != JsonValueKind.Array)
            return argsJson;

        JsonDocument? doc;
        try
        {
            doc = JsonDocument.Parse(argsJson);
        }
        catch
        {
            return argsJson;
        }

        using (doc)
        {
            var root = doc.RootElement;

            // Find required args that are missing from the provided args and have type "string" in schema
            var missingRequired = new List<string>();
            foreach (var req in requiredArr.EnumerateArray())
            {
                var paramName = req.GetString();
                if (paramName is not null &&
                    !root.TryGetProperty(paramName, out _) &&
                    properties.TryGetProperty(paramName, out var propSchema) &&
                    propSchema.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "string")
                {
                    missingRequired.Add(paramName);
                }
            }

            if (missingRequired.Count != 1)
                return argsJson;

            // Find unrecognized args (not in schema properties) that have string values
            var unrecognized = new List<string>();
            foreach (var prop in root.EnumerateObject())
            {
                if (!properties.TryGetProperty(prop.Name, out _) &&
                    prop.Value.ValueKind == JsonValueKind.String)
                {
                    unrecognized.Add(prop.Name);
                }
            }

            if (unrecognized.Count != 1)
                return argsJson;

            var missingName = missingRequired[0];
            var unrecognizedName = unrecognized[0];

            logger?.LogInformation(
                "Inferred missing required arg: {From} → {To}", unrecognizedName, missingName);

            // Rewrite JSON with the remapped arg
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name == unrecognizedName)
                    {
                        writer.WritePropertyName(missingName);
                        prop.Value.WriteTo(writer);
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    /// <summary>
    /// Determines if a property name should be remapped: it must be a known alias,
    /// its canonical target must exist in the schema, the alias itself must NOT
    /// be a canonical name in the schema, and the canonical name must NOT already
    /// be present in the args (to avoid duplicate properties).
    /// </summary>
    private static bool ShouldRemap(string name, JsonElement schemaProperties, JsonElement args)
    {
        return AliasMap.TryGetValue(name, out var canonical) &&
               schemaProperties.TryGetProperty(canonical, out _) &&
               !schemaProperties.TryGetProperty(name, out _) &&
               !args.TryGetProperty(canonical, out _);
    }
}
