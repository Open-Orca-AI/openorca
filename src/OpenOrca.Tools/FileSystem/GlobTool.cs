using System.Text;
using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.FileSystem;

public sealed class GlobTool : IOrcaTool
{
    public string Name => "glob";
    public string Description => "Find files matching a glob pattern (e.g. '**/*.cs', 'src/**/*.json'). Returns matching file paths sorted by modification time (newest first). Use exclude to skip patterns (e.g. '**/node_modules/**').";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "pattern": {
                "type": "string",
                "description": "The glob pattern to match files against"
            },
            "path": {
                "type": "string",
                "description": "The directory to search in. Defaults to current directory."
            },
            "exclude": {
                "type": "string",
                "description": "Glob pattern to exclude from results (e.g. '**/node_modules/**', '**/bin/**')"
            },
            "max_results": {
                "type": "integer",
                "description": "Maximum number of results to return. Defaults to 500."
            }
        },
        "required": ["pattern"]
    }
    """).RootElement;

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var pattern = args.GetProperty("pattern").GetString()!;
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";
        var exclude = args.TryGetProperty("exclude", out var ex) ? ex.GetString() : null;
        var maxResults = args.TryGetProperty("max_results", out var mr) ? mr.GetInt32Lenient(500) : 500;
        path = Path.GetFullPath(path);

        if (!Directory.Exists(path))
            return Task.FromResult(ToolResult.Error($"Directory not found: {path}"));

        try
        {
            var matcher = new Matcher();
            matcher.AddInclude(pattern);
            if (!string.IsNullOrEmpty(exclude))
                matcher.AddExclude(exclude);

            var dirInfo = new DirectoryInfoWrapper(new DirectoryInfo(path));
            var result = matcher.Execute(dirInfo);

            if (!result.HasMatches)
                return Task.FromResult(ToolResult.Success("No files matched the pattern."));

            var files = result.Files
                .Select(f => new
                {
                    Path = f.Path,
                    FullPath = Path.Combine(path, f.Path),
                })
                .Where(f => File.Exists(f.FullPath))
                .Select(f => new
                {
                    f.Path,
                    Modified = File.GetLastWriteTimeUtc(f.FullPath)
                })
                .OrderByDescending(f => f.Modified)
                .Take(maxResults)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"{files.Count} matches:");
            sb.AppendLine();
            foreach (var file in files)
            {
                sb.AppendLine(file.Path);
            }

            return Task.FromResult(ToolResult.Success(sb.ToString()));
        }
        catch (Exception ex2)
        {
            return Task.FromResult(ToolResult.Error($"Error in glob search: {ex2.Message}"));
        }
    }
}
