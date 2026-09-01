using Serilog.Core;
using Serilog.Events;
using System.Text.RegularExpressions;

namespace Statefalse.Api;

/// <summary>
/// Removes credentials from structured log properties before they reach any sink.
/// This is intentionally property-based: callers should continue to use structured
/// logging rather than interpolating secrets into message templates.
/// </summary>
public sealed class SecretRedactingEnricher : ILogEventEnricher
{
    private const string Redacted = "[REDACTED]";

    private static readonly Regex BearerToken = new(
        @"(?<scheme>Bearer)\s+[A-Za-z0-9._~+\-/]+=*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveQueryParameter = new(
        @"(?<key>(?:access_token|refresh_token|client_secret|code|state|id_token|api_key|apikey))=(?<value>[^&#\s]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveHeader = new(
        @"(?<key>(?:Authorization|X-Hub-Signature-256|Cookie))\s*[:=]\s*(?<value>[^,;\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] SensitivePropertyFragments =
    [
        "authorization",
        "accesstoken",
        "refreshtoken",
        "clientsecret",
        "secret",
        "password",
        "apikey",
        "apitoken",
        "signature",
        "cookie",
        "connectionstring"
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToArray())
        {
            var sanitized = SanitizeProperty(property.Key, property.Value);
            if (!ReferenceEquals(sanitized, property.Value))
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, sanitized));
        }
    }

    internal static LogEventPropertyValue SanitizeProperty(string propertyName, LogEventPropertyValue value)
    {
        if (IsSensitiveProperty(propertyName))
            return new ScalarValue(Redacted);

        return value switch
        {
            ScalarValue { Value: string text } => new ScalarValue(SanitizeText(text)),
            SequenceValue sequence => new SequenceValue(sequence.Elements
                .Select(element => SanitizeProperty(propertyName, element))),
            StructureValue structure => new StructureValue(structure.Properties
                .Select(property => new LogEventProperty(
                    property.Name,
                    SanitizeProperty(property.Name, property.Value))), structure.TypeTag),
            _ => value
        };
    }

    internal static string SanitizeText(string text)
    {
        var sanitized = BearerToken.Replace(text, $"${{scheme}} {Redacted}");
        sanitized = SensitiveQueryParameter.Replace(sanitized, $"${{key}}={Redacted}");
        return SensitiveHeader.Replace(sanitized, $"${{key}}: {Redacted}");
    }

    private static bool IsSensitiveProperty(string propertyName)
    {
        var normalized = propertyName.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return SensitivePropertyFragments.Any(normalized.Contains);
    }
}


