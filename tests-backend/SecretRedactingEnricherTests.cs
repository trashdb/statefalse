using Serilog.Core;
using Serilog.Events;

namespace Statefalse.Api.Tests;

public class SecretRedactingEnricherTests
{
    [Fact]
    public void Enrich_RedactsSensitiveProperties()
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Warning,
            null,
            new MessageTemplate("credentials {Authorization} {WebhookSecret} {UserId}", []),
            [
                new LogEventProperty("Authorization", new ScalarValue("Bearer very-secret-token")),
                new LogEventProperty("WebhookSecret", new ScalarValue("github-signature-secret")),
                new LogEventProperty("UserId", new ScalarValue("4242"))
            ]);

        new SecretRedactingEnricher().Enrich(logEvent, new TestPropertyFactory());

        Assert.Equal("[REDACTED]", ((ScalarValue)logEvent.Properties["Authorization"]).Value);
        Assert.Equal("[REDACTED]", ((ScalarValue)logEvent.Properties["WebhookSecret"]).Value);
        Assert.Equal("4242", ((ScalarValue)logEvent.Properties["UserId"]).Value);
    }

    [Fact]
    public void Enrich_RedactsTokensInUrlsAndHeaders()
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplate("request {Details}", []),
            [new LogEventProperty(
                "Details",
                new ScalarValue("GET /hub?access_token=jwt-value&state=oauth-state Authorization: Bearer bearer-value"))]);

        new SecretRedactingEnricher().Enrich(logEvent, new TestPropertyFactory());

        var value = (string)((ScalarValue)logEvent.Properties["Details"]).Value!;
        Assert.DoesNotContain("jwt-value", value);
        Assert.DoesNotContain("oauth-state", value);
        Assert.DoesNotContain("bearer-value", value);
        Assert.Contains("access_token=[REDACTED]", value);
        Assert.Contains("Authorization: [REDACTED]", value);
    }

    [Fact]
    public void Enrich_RedactsNestedSensitiveProperties()
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplate("payload {Payload}", []),
            [new LogEventProperty(
                "Payload",
                new StructureValue(
                [new LogEventProperty("ClientSecret", new ScalarValue("secret-value"))]))]);

        new SecretRedactingEnricher().Enrich(logEvent, new TestPropertyFactory());

        var payload = (StructureValue)logEvent.Properties["Payload"];
        Assert.Equal("[REDACTED]", ((ScalarValue)payload.Properties[0].Value).Value);
    }

    private sealed class TestPropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, new ScalarValue(value));
    }
}


