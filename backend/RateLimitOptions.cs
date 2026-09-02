namespace Statefalse.Api;

public sealed class RateLimitOptions
{
    public RateLimitPolicyOptions Api { get; set; } = new();
    public RateLimitPolicyOptions Oauth { get; set; } = new();
    public RateLimitPolicyOptions OauthLogin { get; set; } = new();
    public RateLimitPolicyOptions OauthToken { get; set; } = new();
    public RateLimitPolicyOptions Action { get; set; } = new();
    public RateLimitPolicyOptions Webhook { get; set; } = new();
    public int RetryAfterSeconds { get; set; } = 60;

    public void Validate()
    {
        ValidatePolicy(nameof(Api), Api);
        ValidatePolicy(nameof(Oauth), Oauth);
        ValidatePolicy(nameof(OauthLogin), OauthLogin);
        ValidatePolicy(nameof(OauthToken), OauthToken);
        ValidatePolicy(nameof(Action), Action);
        ValidatePolicy(nameof(Webhook), Webhook);

        if (RetryAfterSeconds <= 0)
            throw new InvalidOperationException("RateLimiting:RetryAfterSeconds must be greater than zero.");
    }

    private static void ValidatePolicy(string name, RateLimitPolicyOptions policy)
    {
        if (policy.PermitLimit <= 0)
            throw new InvalidOperationException($"RateLimiting:{name}:PermitLimit must be greater than zero.");
        if (policy.WindowSeconds <= 0)
            throw new InvalidOperationException($"RateLimiting:{name}:WindowSeconds must be greater than zero.");
        if (policy.QueueLimit < 0)
            throw new InvalidOperationException($"RateLimiting:{name}:QueueLimit cannot be negative.");
    }
}

public sealed class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; }
}
