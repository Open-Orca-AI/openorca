using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOrca.Cli.Repl;
using OpenOrca.Core.Configuration;
using Xunit;

namespace OpenOrca.Cli.Tests;

public class MaxTokensNegotiatorTests
{
    private static OrcaConfig CreateConfig(int? maxTokens = null) => new()
    {
        LmStudio = new LmStudioConfig
        {
            BaseUrl = "http://localhost:1234/v1",
            ApiKey = "test-key",
            Model = "test-model",
            MaxTokens = maxTokens
        }
    };

    [Fact]
    public async Task NegotiateAsync_UserHasExplicitMaxTokens_ReturnsNull()
    {
        var config = CreateConfig(maxTokens: 2048);
        var handler = new FakeHandler(HttpStatusCode.OK, "");
        using var client = new HttpClient(handler);
        var negotiator = new MaxTokensNegotiator(client);

        var result = await negotiator.NegotiateAsync(config, NullLogger.Instance, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task NegotiateAsync_ApiAcceptsFirstValue_Returns32768()
    {
        var config = CreateConfig();
        var handler = new FakeHandler(HttpStatusCode.OK, "");
        using var client = new HttpClient(handler);
        var negotiator = new MaxTokensNegotiator(client);

        var result = await negotiator.NegotiateAsync(config, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(32768, result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task NegotiateAsync_ApiRejectsWithParseableLimit_ReturnsParsedValue()
    {
        var config = CreateConfig();
        var errorJson = """{"error":{"message":"max_tokens must be less than or equal to `8192`","type":"invalid_request_error"}}""";
        var handler = new FakeHandler(HttpStatusCode.BadRequest, errorJson);
        using var client = new HttpClient(handler);
        var negotiator = new MaxTokensNegotiator(client);

        var result = await negotiator.NegotiateAsync(config, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(8192, result);
        Assert.Equal(1, handler.RequestCount); // Only one probe needed
    }

    [Fact]
    public async Task NegotiateAsync_ApiRejectsWithParseableLimit_NoBackticks_ReturnsParsedValue()
    {
        var config = CreateConfig();
        var errorJson = """{"error":"max_tokens must be less than or equal to 4096"}""";
        var handler = new FakeHandler(HttpStatusCode.BadRequest, errorJson);
        using var client = new HttpClient(handler);
        var negotiator = new MaxTokensNegotiator(client);

        var result = await negotiator.NegotiateAsync(config, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(4096, result);
    }

    [Fact]
    public async Task NegotiateAsync_ApiRejectsWithoutLimit_FallsBackToNextAccepted()
    {
        var config = CreateConfig();
        // First three probes fail with unparseable error, fourth (4096) succeeds
        var handler = new SequenceHandler(
            (HttpStatusCode.BadRequest, "unknown error"),
            (HttpStatusCode.BadRequest, "unknown error"),
            (HttpStatusCode.BadRequest, "unknown error"),
            (HttpStatusCode.OK, ""));
        using var client = new HttpClient(handler);
        var negotiator = new MaxTokensNegotiator(client);

        var result = await negotiator.NegotiateAsync(config, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(4096, result);
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task NegotiateAsync_AllProbesFail_ReturnsNull()
    {
        var config = CreateConfig();
        var handler = new FakeHandler(HttpStatusCode.BadRequest, "unknown error");
        using var client = new HttpClient(handler);
        var negotiator = new MaxTokensNegotiator(client);

        var result = await negotiator.NegotiateAsync(config, NullLogger.Instance, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(4, handler.RequestCount); // Tried all 4 fallback values
    }

    [Theory]
    [InlineData("""{"error":{"message":"max_tokens must be less than or equal to `8192`"}}""", 8192)]
    [InlineData("""{"error":{"message":"max_tokens must be less than or equal to 131072"}}""", 131072)]
    [InlineData("""{"error":"max_tokens must be less than or equal to `16384`"}""", 16384)]
    [InlineData("max_tokens must be less than or equal to 4096", 4096)]
    public void ParseLimitFromError_ValidPatterns_ReturnsExpectedValue(string errorBody, int expected)
    {
        var result = MaxTokensNegotiator.ParseLimitFromError(errorBody);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("some random error")]
    [InlineData("""{"error":{"message":"rate limit exceeded"}}""")]
    [InlineData("max_tokens is too large")]
    public void ParseLimitFromError_NoParseable_ReturnsNull(string errorBody)
    {
        var result = MaxTokensNegotiator.ParseLimitFromError(errorBody);
        Assert.Null(result);
    }

    /// <summary>
    /// Fake HTTP handler that always returns the same response.
    /// </summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;
        public int RequestCount { get; private set; }

        public FakeHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body)
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Fake HTTP handler that returns a sequence of responses.
    /// </summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly (HttpStatusCode Code, string Body)[] _responses;
        public int RequestCount { get; private set; }

        public SequenceHandler(params (HttpStatusCode, string)[] responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = RequestCount < _responses.Length ? RequestCount : _responses.Length - 1;
            RequestCount++;
            var (code, body) = _responses[index];
            var response = new HttpResponseMessage(code)
            {
                Content = new StringContent(body)
            };
            return Task.FromResult(response);
        }
    }
}
