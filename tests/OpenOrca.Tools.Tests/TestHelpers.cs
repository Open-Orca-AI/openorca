using System.Text.Json;

namespace OpenOrca.Tools.Tests;

internal static class TestHelpers
{
    public static JsonElement MakeArgs(string json) =>
        JsonDocument.Parse(json).RootElement;

    public static string EscapePath(string path) =>
        path.Replace("\\", "\\\\");
}
