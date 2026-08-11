using System.Text.Json;
using Statefalse.Application;
using Statefalse.Domain.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Statefalse.Api.Tests;

public class QueryInterpretationServiceTests
{
    private readonly FakeAiClient _ai = new();

    private QueryInterpretationService CreateService()
        => new(_ai, NullLogger<QueryInterpretationService>.Instance);

    private static InterpretRequest Request(string query) => new()
    {
        Query = query,
        GitHubId = 1,
        ApiKey = "sk-test",
        AiProvider = "openai",
        Model = "gpt-4o"
    };

    [Fact]
    public async Task Interpret_ValidJson_ParsesAction()
    {
        _ai.Reply = """{"action":"checkoutBranch","message":"Cambiando a rama…","params":{"branch":"fix/x"}}""";

        var result = await CreateService().InterpretAsync(Request("checkout fix/x"), null);

        var response = Assert.IsType<InterpretResponse>(result);
        Assert.Equal("checkoutBranch", response.Action);
        Assert.Equal("Cambiando a rama…", response.Message);
        Assert.Equal("fix/x", response.Params!["branch"]);
    }

    [Fact]
    public async Task Interpret_NullReply_ReturnsUnknownWithMessage()
    {
        _ai.Reply = null;

        var result = await CreateService().InterpretAsync(Request("hmm"), null);
        var json = JsonSerializer.Serialize(result);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("unknown", doc.RootElement.GetProperty("action").GetString());
        Assert.Contains("Could not interpret", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Interpret_InvalidJson_ReturnsUnknownWithRawReply()
    {
        _ai.Reply = "not json at all";

        var result = await CreateService().InterpretAsync(Request("hmm"), null);

        var response = Assert.IsType<InterpretResponse>(result);
        Assert.Equal("unknown", response.Action);
        Assert.Equal("not json at all", response.Message);
    }

    [Fact]
    public async Task Interpret_SendsQueryInPrompt()
    {
        _ai.Reply = """{"action":"openPRs","message":"Abriendo PRs"}""";

        await CreateService().InterpretAsync(Request("ver mis pull requests"), null);

        Assert.Contains("ver mis pull requests", _ai.LastRequest!.UserPrompt);
        Assert.Equal("openai", _ai.LastRequest.Provider);
        Assert.Equal("gpt-4o", _ai.LastRequest.Model);
    }

    private sealed class FakeAiClient : IAiProviderClient
    {
        public string? Reply { get; set; }
        public AiRequest? LastRequest { get; private set; }

        public Task<string?> CompleteAsync(AiRequest request)
        {
            LastRequest = request;
            return Task.FromResult(Reply);
        }
    }
}
