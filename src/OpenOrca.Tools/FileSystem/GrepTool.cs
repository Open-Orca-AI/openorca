using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using OpenOrca.Tools.Abstractions;

namespace OpenOrca.Tools.FileSystem;

public sealed class GrepTool : IOrcaTool
{
    public string Name => "grep";
    public string Description => "Search file contents using a regex pattern. Supports glob file filters and context lines. Use output_mode to control output: 'content' (default) shows matching lines, 'files_only' shows file paths, 'count' shows match counts per file.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "pattern": {
                "type": "string",
                "description": "The regex pattern to search for"
            },
            "path": {
                "type": "string",
                "description": "The directory or file to search in. Defaults to current directory."
            },
            "glob": {
                "type": "string",
                "description": "Optional glob pattern to filter files (e.g. '*.cs', '**/*.json')"
            },
            "context": {
                "type": "integer",
                "description": "Number of context lines before and after each match. Defaults to 0."
            },
            "case_insensitive": {
                "type": "boolean",
                "description": "Whether to search case-insensitively. Defaults to false."
            },
            "output_mode": {
                "type": "string",
                "enum": ["content", "files_only", "count"],
                "description": "Output mode: 'content' shows matching lines (default), 'files_only' returns only file paths, 'count' returns files with match counts."
            },
            "max_results": {
                "type": "integer",
                "description": "Maximum number of matches to return. Defaults to 500."
            }
        },
        "required": ["pattern"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var pattern = args.GetProperty("pattern").GetString()!;
        var path = args.TryGetProperty("path", out var p) ? p.GetString() ?? "." : ".";
        var glob = args.TryGetProperty("glob", out var g) ? g.GetString() : null;
        var context = args.TryGetProperty("context", out var c) ? c.GetInt32() : 0;
        var caseInsensitive = args.TryGetProperty("case_insensitive", out var ci) && ci.GetBoolean();
        var outputMode = args.TryGetProperty("output_mode", out var om) ? om.GetString() ?? "content" : "content";
        var maxResults = args.TryGetProperty("max_results", out var mr) ? mr.GetInt32() : 500;

        path = Path.GetFullPath(path);

        var regexOptions = RegexOptions.Compiled;
        if (caseInsensitive) regexOptions |= RegexOptions.IgnoreCase;

        Regex regex;
        try
        {
            regex = new Regex(pattern, regexOptions);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Invalid regex: {ex.Message}");
        }

        var files = GetFiles(path, glob);
        var sb = new StringBuilder();
        var matchCount = 0;
        var fileMatchCounts = new Dictionary<string, int>();

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            if (matchCount >= maxResults) break;

            try
            {
                var lines = await File.ReadAllLinesAsync(file, ct);
                var relativePath = Path.GetRelativePath(path, file);
                var fileHits = 0;

                for (var i = 0; i < lines.Length && matchCount < maxResults; i++)
                {
                    if (!regex.IsMatch(lines[i])) continue;

                    matchCount++;
                    fileHits++;

                    if (outputMode == "content")
                    {
                        var startLine = Math.Max(0, i - context);
                        var endLine = Math.Min(lines.Length - 1, i + context);

                        sb.AppendLine($"{relativePath}:{i + 1}:");

                        for (var j = startLine; j <= endLine; j++)
                        {
                            var prefix = j == i ? ">" : " ";
                            sb.AppendLine($"  {prefix} {j + 1}\t{lines[j]}");
                        }

                        sb.AppendLine();
                    }
                }

                if (fileHits > 0)
                    fileMatchCounts[relativePath] = fileHits;
            }
            catch
            {
                // Skip files that can't be read (binary, permissions, etc.)
            }
        }

        if (matchCount == 0)
            return ToolResult.Success("No matches found.");

        return outputMode switch
        {
            "files_only" => FormatFilesOnly(fileMatchCounts, matchCount, maxResults),
            "count" => FormatCount(fileMatchCounts, matchCount, maxResults),
            _ => FormatContent(sb, matchCount, maxResults)
        };
    }

    private static ToolResult FormatContent(StringBuilder sb, int matchCount, int maxResults)
    {
        var header = matchCount >= maxResults
            ? $"Found {maxResults}+ matches (truncated):\n\n"
            : $"Found {matchCount} match(es):\n\n";
        return ToolResult.Success(header + sb);
    }

    private static ToolResult FormatFilesOnly(Dictionary<string, int> fileMatchCounts, int matchCount, int maxResults)
    {
        var sb = new StringBuilder();
        var truncated = matchCount >= maxResults ? " (truncated)" : "";
        sb.AppendLine($"{fileMatchCounts.Count} files with {matchCount} total matches{truncated}:");
        sb.AppendLine();
        foreach (var kvp in fileMatchCounts.OrderBy(f => f.Key))
            sb.AppendLine(kvp.Key);
        return ToolResult.Success(sb.ToString());
    }

    private static ToolResult FormatCount(Dictionary<string, int> fileMatchCounts, int matchCount, int maxResults)
    {
        var sb = new StringBuilder();
        var truncated = matchCount >= maxResults ? " (truncated)" : "";
        sb.AppendLine($"{fileMatchCounts.Count} files with {matchCount} total matches{truncated}:");
        sb.AppendLine();
        foreach (var kvp in fileMatchCounts.OrderByDescending(f => f.Value))
            sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        return ToolResult.Success(sb.ToString());
    }

    private static IEnumerable<string> GetFiles(string path, string? glob)
    {
        if (File.Exists(path))
            return [path];

        if (!Directory.Exists(path))
            return [];

        if (!string.IsNullOrEmpty(glob))
        {
            var matcher = new Matcher();
            matcher.AddInclude(glob);
            var dirInfo = new DirectoryInfoWrapper(new DirectoryInfo(path));
            var result = matcher.Execute(dirInfo);
            return result.Files.Select(f => Path.Combine(path, f.Path));
        }

        // Default: search common text file types
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".cs" or ".js" or ".ts" or ".tsx" or ".jsx" or ".py" or ".json" or ".xml"
                    or ".yaml" or ".yml" or ".md" or ".txt" or ".html" or ".css"
                    or ".csproj" or ".sln" or ".props" or ".targets" or ".sh"
                    or ".bash" or ".ps1" or ".toml" or ".cfg" or ".ini"
                    or ".go" or ".rs" or ".java" or ".kt" or ".swift" or ".rb"
                    or ".php" or ".sql" or ".dockerfile" or ".gitignore"
                    or ".vue" or ".svelte" or ".proto" or ".graphql" or ".env"
                    or ".gradle" or ".tf" or ".hcl";
            });
    }
}
