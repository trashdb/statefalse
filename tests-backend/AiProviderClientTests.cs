using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Statefalse.Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace Statefalse.Api.Tests;

public class AiProviderClientTests
{
    private static AiProviderClient CreateClient(FakeHttpHandler handler)
        => new(NullLogger<AiProviderClient>.Instance, handler);

    private static AiRequest OpenAiRequest() => new(
        SystemPrompt: "sys", UserPrompt: "user", ApiKey: "sk-test", Provider: "openai",
        Model: "gpt-4o", OAuthToken: null);

    [Fact]
    public async Task Complete_OpenAi_ReturnsFirstChoiceContent()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"hello world"}}]}""");
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(OpenAiRequest());

        Assert.Equal("hello world", result);
        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.LastRequestUri);
        Assert.Equal("Bearer sk-test", handler.LastAuth);
    }

    [Fact]
    public async Task Complete_CopilotViaOAuth_UsesGithubCopilotUrl()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"copilot answer"}}]}""");
        var client = CreateClient(handler);

        var request = OpenAiRequest() with { ApiKey = null, OAuthToken = "gho_1" };
        var result = await client.CompleteAsync(request);

        Assert.Equal("copilot answer", result);
        Assert.Equal("https://api.githubcopilot.com/chat/completions", handler.LastRequestUri);
    }

    [Fact]
    public async Task Complete_NoToken_ReturnsNull()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);

        var request = OpenAiRequest() with { ApiKey = null, OAuthToken = null };
        var result = await client.CompleteAsync(request);

        Assert.Null(result);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task Complete_NonSuccess_ReturnsNull()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.BadGateway, "oops");
        var client = CreateClient(handler);

        Assert.Null(await client.CompleteAsync(OpenAiRequest()));
    }

    [Fact]
    public async Task Complete_Anthropic_SendsApiKeyHeaderAndParsesText()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            """{"content":[{"type":"text","text":"anthropic answer"}]}""");
        var client = CreateClient(handler);

        var request = OpenAiRequest() with { Provider = "anthropic" };
        var result = await client.CompleteAsync(request);

        Assert.Equal("anthropic answer", result);
        Assert.Equal("https://api.anthropic.com/v1/messages", handler.LastRequestUri);
        Assert.Equal("sk-test", handler.LastApiKey);
        Assert.Contains("anthropic-version", handler.LastRequestHeaders!.Select(h => h.Key));
    }

    [Fact]
    public async Task Complete_AnthropicNoApiKey_ReturnsNull()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);

        var request = OpenAiRequest() with { Provider = "anthropic", ApiKey = null };
        Assert.Null(await client.CompleteAsync(request));
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task Complete_Gemini_ParsesCandidatesText()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"gemini answer"}]}}]}""");
        var client = CreateClient(handler);

        var request = OpenAiRequest() with { Provider = "gemini" };
        var result = await client.CompleteAsync(request);

        Assert.Equal("gemini answer", result);
        Assert.Contains("generativelanguage.googleapis.com", handler.LastRequestUri);
        Assert.Contains("key=sk-test", handler.LastRequestUri);
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeHttpHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public string? LastRequestUri { get; private set; }
        public string? LastAuth { get; private set; }
        public string? LastApiKey { get; private set; }
        public HttpRequestHeaders? LastRequestHeaders { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri!.AbsoluteUri;
            LastRequestHeaders = request.Headers;
            LastAuth = request.Headers.Authorization?.ToString();
            if (request.Headers.TryGetValues("x-api-key", out var keys))
                LastApiKey = keys.First();

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
