using System.Text.Json;

namespace OpenOrca.Tools.Utilities;

/// <summary>
/// Extension methods for JsonElement that tolerate LLMs sending values as the wrong JSON type.
/// For example, sending "50" (string) instead of 50 (number) for an integer parameter.
/// </summary>
public static class JsonElementExtensions
{
    /// <summary>
    /// Get an int from a JsonElement, tolerating string-encoded numbers from LLMs.
    /// </summary>
    public static int GetInt32Lenient(this JsonElement element, int defaultValue = 0)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt32(),
            JsonValueKind.String when int.TryParse(element.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Get a bool from a JsonElement, tolerating string-encoded booleans from LLMs.
    /// </summary>
    public static bool GetBooleanLenient(this JsonElement element, bool defaultValue = false)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }
}
