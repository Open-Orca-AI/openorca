using System.Net.Http.Headers;

namespace OpenOrca.Tools.Utilities;

/// <summary>
/// Provides a shared, long-lived HttpClient for web tools.
/// </summary>
public static class HttpHelper
{
    private static readonly Lazy<HttpClient> LazyClient = new(() =>
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("OpenOrca", "0.12.1")); // Keep in sync with Directory.Build.props <Version>
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/plain"));
        return client;
    });

    public static HttpClient Client => LazyClient.Value;
}
