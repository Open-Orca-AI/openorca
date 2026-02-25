using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.Web;

public sealed class WebSearchTool : IOrcaTool
{
    public ILogger? Logger { get; set; }

    public string Name => "web_search";
    public string Description => "Search the web using DuckDuckGo and return result titles, URLs, and snippets. No API key required. Use for finding documentation, answers, package info, and current information.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "query": {
                "type": "string",
                "description": "The search query"
            },
            "max_results": {
                "type": "integer",
                "description": "Maximum number of results to return. Defaults to 5."
            }
        },
        "required": ["query"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var query = args.GetProperty("query").GetString()!;
        var maxResults = args.TryGetProperty("max_results", out var mr) ? mr.GetInt32Lenient(5) : 5;

        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://html.duckduckgo.com/html/?q={encodedQuery}";

            await DomainRateLimiter.ThrottleAsync(url, ct);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "text/html");

            var response = await HttpHelper.Client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return ToolResult.Error($"Search failed: HTTP {(int)response.StatusCode}");

            var html = await response.Content.ReadAsStringAsync(ct);
            var results = ParseResults(html, maxResults);

            if (results.Count == 0)
            {
                // Distinguish between no results and a parsing failure
                var hasResultMarkers = html.Contains("result__a") || html.Contains("result__snippet");
                return hasResultMarkers
                    ? ToolResult.Error("Search returned results but failed to parse them. DuckDuckGo HTML structure may have changed.")
                    : ToolResult.Success($"No results found for: {query}");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Search results for: {query}");
            sb.AppendLine($"{results.Count} results:");
            sb.AppendLine();

            for (var i = 0; i < results.Count; i++)
            {
                var (title, resultUrl, snippet) = results[i];
                sb.AppendLine($"{i + 1}. {title}");
                sb.AppendLine($"   {resultUrl}");
                if (!string.IsNullOrWhiteSpace(snippet))
                    sb.AppendLine($"   {snippet}");
                sb.AppendLine();
            }

            return ToolResult.Success(sb.ToString());
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolResult.Error("Search request timed out");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Search error: {ex.Message}");
        }
    }

    // Multiple regex patterns for resilience against DuckDuckGo HTML changes.
    // Order-agnostic: covers class-before-href and href-before-class.
    // Uses [^>]*? (lazy) to avoid overshooting into adjacent tags.
    private static readonly Regex[] TitleLinkPatterns =
    [
        // class="result__a" before href (double quotes)
        new(@"<a\s+[^>]*?class=""result__a""[^>]*?href=""([^""]+)""[^>]*?>(.*?)</a>", RegexOptions.Singleline | RegexOptions.Compiled),
        // href before class (double quotes)
        new(@"<a\s+[^>]*?href=""([^""]+)""[^>]*?class=""result__a""[^>]*?>(.*?)</a>", RegexOptions.Singleline | RegexOptions.Compiled),
        // Single quote variants
        new(@"<a\s+[^>]*?class='result__a'[^>]*?href='([^']+)'[^>]*?>(.*?)</a>", RegexOptions.Singleline | RegexOptions.Compiled),
        new(@"<a\s+[^>]*?href='([^']+)'[^>]*?class='result__a'[^>]*?>(.*?)</a>", RegexOptions.Singleline | RegexOptions.Compiled),
    ];

    // Snippet patterns — use \w+ for closing tag to handle any container element
    private static readonly Regex[] SnippetPatterns =
    [
        new(@"class=""result__snippet""[^>]*>(.*?)</\w+>", RegexOptions.Singleline | RegexOptions.Compiled),
        new(@"class='result__snippet'[^>]*>(.*?)</\w+>", RegexOptions.Singleline | RegexOptions.Compiled),
    ];

    private static readonly Regex UddgPattern = new(@"uddg=([^&]+)", RegexOptions.Compiled);

    private static List<(string Title, string Url, string Snippet)> ParseResults(string html, int maxResults)
    {
        var results = new List<(string, string, string)>();

        // Try each title/link pattern until one yields results
        MatchCollection? resultBlocks = null;
        foreach (var pattern in TitleLinkPatterns)
        {
            var matches = pattern.Matches(html);
            if (matches.Count > 0)
            {
                resultBlocks = matches;
                break;
            }
        }

        if (resultBlocks is null || resultBlocks.Count == 0)
            return results;

        // Try each snippet pattern
        MatchCollection? snippetBlocks = null;
        foreach (var pattern in SnippetPatterns)
        {
            var matches = pattern.Matches(html);
            if (matches.Count > 0)
            {
                snippetBlocks = matches;
                break;
            }
        }

        for (var i = 0; i < Math.Min(resultBlocks.Count, maxResults); i++)
        {
            var match = resultBlocks[i];
            var rawUrl = match.Groups[1].Value;
            var title = StripTags(match.Groups[2].Value);

            // DuckDuckGo wraps URLs in a redirect — extract the actual URL
            var actualUrl = rawUrl;
            var uddgMatch = UddgPattern.Match(rawUrl);
            if (uddgMatch.Success)
                actualUrl = Uri.UnescapeDataString(uddgMatch.Groups[1].Value);

            var snippet = snippetBlocks is not null && i < snippetBlocks.Count
                ? StripTags(snippetBlocks[i].Groups[1].Value)
                : "";

            results.Add((title.Trim(), actualUrl.Trim(), snippet.Trim()));
        }

        return results;
    }

    private static string StripTags(string html)
    {
        var text = Regex.Replace(html, @"<[^>]+>", "");
        return WebUtility.HtmlDecode(text).Trim();
    }
}
