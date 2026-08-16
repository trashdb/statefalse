using System.Net;
using System.Text;
using Statefalse.Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace Statefalse.Api.Tests;

public class AiProviderClientTests
{
    private static AiProviderClient CreateClient(FakeHttpHandler handler)
        => new(NullLogger<AiProviderClient>.Instance, handler);

    private static AiRequest CopilotRequest(string? oauthToken = "gho_test") => new(
        SystemPrompt: "sys", UserPrompt: "user", OAuthToken: oauthToken);

    [Fact]
    public async Task Complete_CopilotViaOAuth_ReturnsFirstChoiceContent()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"copilot answer"}}]}""");
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(CopilotRequest());

        Assert.Equal("copilot answer", result);
        Assert.Equal("https://api.githubcopilot.com/chat/completions", handler.LastRequestUri);
        Assert.Equal("Bearer gho_test", handler.LastAuth);
    }

    [Fact]
    public async Task Complete_NoToken_ReturnsNull()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(CopilotRequest(null));

        Assert.Null(result);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task Complete_NonSuccess_ReturnsNull()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.BadGateway, "oops");
        var client = CreateClient(handler);

        Assert.Null(await client.CompleteAsync(CopilotRequest()));
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri!.AbsoluteUri;
            LastAuth = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
