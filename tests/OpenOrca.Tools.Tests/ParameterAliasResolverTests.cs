using System.Text.Json;
using OpenOrca.Tools.Utilities;
using Xunit;

namespace OpenOrca.Tools.Tests;

public class ParameterAliasResolverTests
{
    private static JsonElement MakeSchema(params string[] propertyNames)
    {
        var props = string.Join(", ", propertyNames.Select(n => $"\"{n}\": {{\"type\": \"string\"}}"));
        return JsonDocument.Parse($"{{\"type\": \"object\", \"properties\": {{{props}}}}}").RootElement;
    }

    [Fact]
    public void FilePath_RemapsToPath()
    {
        var schema = MakeSchema("path");
        var result = ParameterAliasResolver.ResolveAliases("{\"file_path\": \"/tmp/foo.txt\"}", schema);
        var parsed = JsonDocument.Parse(result).RootElement;

        Assert.True(parsed.TryGetProperty("path", out var val));
        Assert.Equal("/tmp/foo.txt", val.GetString());
        Assert.False(parsed.TryGetProperty("file_path", out _));
    }

    [Fact]
    public void Directory_RemapsToPath()
    {
        var schema = MakeSchema("path");
        var result = ParameterAliasResolver.ResolveAliases("{\"directory\": \"/tmp\"}", schema);
        var parsed = JsonDocument.Parse(result).RootElement;

        Assert.True(parsed.TryGetProperty("path", out var val));
        Assert.Equal("/tmp", val.GetString());
    }

    [Fact]
    public void NoRemap_WhenCanonicalAlreadyPresent()
    {
        var schema = MakeSchema("path");
        var args = "{\"path\": \"/real.txt\", \"file_path\": \"/alias.txt\"}";
        var result = ParameterAliasResolver.ResolveAliases(args, schema);
        var parsed = JsonDocument.Parse(result).RootElement;

        // path was already present, so file_path should NOT be remapped
        Assert.True(parsed.TryGetProperty("path", out var val));
        Assert.Equal("/real.txt", val.GetString());
        Assert.True(parsed.TryGetProperty("file_path", out _));
    }

    [Fact]
    public void NoRemap_WhenAliasTargetNotInSchema()
    {
        // Schema has "url" but not "path", so "file_path" should not remap
        var schema = MakeSchema("url");
        var args = "{\"file_path\": \"/tmp/foo.txt\"}";
        var result = ParameterAliasResolver.ResolveAliases(args, schema);

        Assert.Equal(args, result);
    }

    [Fact]
    public void NoRemap_WhenAliasIsCanonicalNameInSchema()
    {
        // Schema has both "directory" and "path" — "directory" is a real param, not an alias
        var schema = MakeSchema("path", "directory");
        var args = "{\"directory\": \"/tmp\"}";
        var result = ParameterAliasResolver.ResolveAliases(args, schema);
        var parsed = JsonDocument.Parse(result).RootElement;

        Assert.True(parsed.TryGetProperty("directory", out var val));
        Assert.Equal("/tmp", val.GetString());
        Assert.False(parsed.TryGetProperty("path", out _));
    }

    [Fact]
    public void Passthrough_CorrectArgs()
    {
        var schema = MakeSchema("path", "offset", "limit");
        var args = "{\"path\": \"/tmp/foo.txt\", \"offset\": 1}";
        var result = ParameterAliasResolver.ResolveAliases(args, schema);

        Assert.Equal(args, result);
    }

    [Fact]
    public void Passthrough_EmptyArgs()
    {
        var schema = MakeSchema("path");
        Assert.Equal("{}", ParameterAliasResolver.ResolveAliases("{}", schema));
        Assert.Equal("", ParameterAliasResolver.ResolveAliases("", schema));
    }

    [Fact]
    public void MultipleAliases_ResolvedInOneCall()
    {
        var schema = MakeSchema("old_string", "new_string", "path");
        var args = "{\"file_path\": \"/f.txt\", \"find\": \"hello\", \"replacement\": \"world\"}";
        var result = ParameterAliasResolver.ResolveAliases(args, schema);
        var parsed = JsonDocument.Parse(result).RootElement;

        Assert.True(parsed.TryGetProperty("path", out var p));
        Assert.Equal("/f.txt", p.GetString());
        Assert.True(parsed.TryGetProperty("old_string", out var o));
        Assert.Equal("hello", o.GetString());
        Assert.True(parsed.TryGetProperty("new_string", out var n));
        Assert.Equal("world", n.GetString());
    }

    [Fact]
    public void Instructions_RemapsToTask_ForSpawnAgent()
    {
        var schema = MakeSchema("task", "agent_type");
        var args = "{\"instructions\": \"review this code\", \"agent_type\": \"reviewer\"}";
        var result = ParameterAliasResolver.ResolveAliases(args, schema);
        var parsed = JsonDocument.Parse(result).RootElement;

        Assert.True(parsed.TryGetProperty("task", out var val));
        Assert.Equal("review this code", val.GetString());
        Assert.True(parsed.TryGetProperty("agent_type", out _));
    }

    [Fact]
    public void OtherProperties_PreservedUnchanged()
    {
        var schema = MakeSchema("path", "offset", "limit");
        var args = "{\"file_path\": \"/tmp/foo.txt\", \"offset\": 10, \"limit\": 50}";
        var result = ParameterAliasResolver.ResolveAliases(args, schema);
        var parsed = JsonDocument.Parse(result).RootElement;

        Assert.True(parsed.TryGetProperty("path", out _));
        Assert.Equal(10, parsed.GetProperty("offset").GetInt32());
        Assert.Equal(50, parsed.GetProperty("limit").GetInt32());
    }

    [Fact]
    public void Cmd_RemapsToCommand()
    {
        var schema = MakeSchema("command");
        var args = "{\"cmd\": \"ls -la\"}";
        var result = ParameterAliasResolver.ResolveAliases(args, schema);
        var parsed = JsonDocument.Parse(result).RootElement;

        Assert.True(parsed.TryGetProperty("command", out var val));
        Assert.Equal("ls -la", val.GetString());
    }

    [Fact]
    public void NoRemap_WhenCanonicalAlreadyPresent_DuplicateAlias()
    {
        // Both "path" (canonical) and "dir" (alias) present — alias should NOT remap
        var schema = MakeSchema("path");
        var args = "{\"path\": \"/real.txt\", \"dir\": \"/alias\"}";
        var result = ParameterAliasResolver.ResolveAliases(args, schema);
        var parsed = JsonDocument.Parse(result).RootElement;

        Assert.Equal("/real.txt", parsed.GetProperty("path").GetString());
        Assert.True(parsed.TryGetProperty("dir", out _));
    }
}
