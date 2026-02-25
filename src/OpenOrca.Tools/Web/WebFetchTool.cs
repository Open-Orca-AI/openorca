using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenOrca.Tools.Abstractions;
using OpenOrca.Tools.Utilities;

namespace OpenOrca.Tools.Web;

public sealed class WebFetchTool : IOrcaTool
{
    public string Name => "web_fetch";
    public string Description => "Fetch the content of a URL and return it as text. HTML is stripped to plain text by default. Set raw=true to get the original response body. Useful for reading documentation, APIs, and web pages.";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;

    public JsonElement ParameterSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "url": {
                "type": "string",
                "description": "The URL to fetch"
            },
            "max_length": {
                "type": "integer",
                "description": "Maximum characters to return. Defaults to 20000."
            },
            "raw": {
                "type": "boolean",
                "description": "Return the raw response body without HTML stripping. Defaults to false."
            }
        },
        "required": ["url"]
    }
    """).RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var url = args.GetProperty("url").GetString()!;
        var maxLength = args.TryGetProperty("max_length", out var ml) ? ml.GetInt32Lenient(20000) : 20000;
        var raw = args.TryGetProperty("raw", out var r) && r.GetBooleanLenient();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return ToolResult.Error("Invalid URL. Must be an absolute http or https URL.");
        }

        try
        {
            await DomainRateLimiter.ThrottleAsync(url, ct);

            var response = await HttpHelper.Client.GetAsync(uri, ct);

            if (!response.IsSuccessStatusCode)
                return ToolResult.Error($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!raw)
            {
                body = StripHtml(body);
            }

            if (body.Length > maxLength)
                body = body[..maxLength] + $"\n\n... ({body.Length - maxLength} chars truncated)";

            return ToolResult.Success($"URL: {url}\nLength: {body.Length} chars\n\n{body}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolResult.Error($"Request timed out fetching {url}");
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Error($"HTTP error fetching {url}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Error fetching {url}: {ex.Message}");
        }
    }

    private static string StripHtml(string html)
    {
        // Remove script and style blocks
        var text = Regex.Replace(html, @"<(script|style)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        // Remove HTML tags
        text = Regex.Replace(text, @"<[^>]+>", " ");
        // Decode common entities
        text = WebUtility.HtmlDecode(text);
        // Collapse whitespace
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}
