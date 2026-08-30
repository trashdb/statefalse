using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Serilog;
using Scalar.AspNetCore;
using Statefalse.Api;
using Statefalse.Application;
using Statefalse.Domain;
using Statefalse.Infrastructure;
using Statefalse.Infrastructure.Data;
using Statefalse.Infrastructure.Hubs;
using Statefalse.Infrastructure.Repositories;
using Statefalse.Infrastructure.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/statefalse-api-.log", rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30, restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, config) =>
    {
        config.ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/statefalse-api-.log", rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30);
    });

    // Database — in tests the host is configured with Database:Provider=Sqlite
    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.Equals(builder.Configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase))
            options.UseSqlite(connectionString);
        else
            options.UseNpgsql(connectionString);
    });

    // SignalR
    builder.Services.AddSignalR(options =>
    {
        options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    });

    // HttpClient for GitHub OAuth. OAuth exchanges must fail fast rather than
    // holding an anonymous request open indefinitely.
    builder.Services.AddHttpClient<GitHubOAuthService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);
    });
    builder.Services.AddHttpClient<IGitHubClient, GitHubClient>(client =>
    {
        client.BaseAddress = new Uri("https://api.github.com");
    });

    // Application services
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<IGitHubUserRepository, GitHubUserRepository>();
    builder.Services.AddScoped<IPunishmentEventRepository, PunishmentEventRepository>();
    builder.Services.AddScoped<ICheckSuiteEventRepository, CheckSuiteEventRepository>();
    builder.Services.AddScoped<IPullRequestEventRepository, PullRequestEventRepository>();
    builder.Services.AddScoped<IWorkflowRunRepository, WorkflowRunRepository>();
    builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
    builder.Services.AddScoped<IGitHubTokenResolver, GitHubTokenResolver>();
    builder.Services.AddSingleton<IGitHubCredentialProtector, GitHubCredentialProtector>();
    builder.Services.AddScoped<GitHubCredentialMigrationService>();
    builder.Services.AddScoped<ISignalRNotifier, SignalRNotifier>();
    builder.Services.AddScoped<PullRequestSyncService>();
    builder.Services.AddScoped<PullRequestQueryService>();
    builder.Services.AddScoped<PullRequestActionService>();
    builder.Services.AddScoped<PullRequestSubscriptionService>();
    builder.Services.AddScoped<WebhookService>();
    builder.Services.AddScoped<IAiProviderClient, AiProviderClient>();
    builder.Services.AddScoped<PrPreviewService>();
    builder.Services.AddScoped<QueryInterpretationService>();
    builder.Services.AddScoped<GitHubApiService>();
    builder.Services.AddScoped<WorkflowService>();
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
    builder.Services.AddSingleton<OAuthStateStore>();
    builder.Services.AddSingleton<OAuthCodeStore>();
    builder.Services.AddScoped<PunishmentService>();

    // Periodic data maintenance (stuck/superseded workflow runs)
    builder.Services.AddHostedService<WorkflowCleanupService>();

    // Webhook handlers (dispatched by WebhookService via X-GitHub-Event)
    builder.Services.AddScoped<IWebhookHandler, WorkflowRunWebhookHandler>();
    builder.Services.AddScoped<IWebhookHandler, CheckSuiteWebhookHandler>();
    builder.Services.AddScoped<IWebhookHandler, PullRequestWebhookHandler>();
    builder.Services.AddScoped<IWebhookHandler, PullRequestReviewWebhookHandler>();
    builder.Services.AddScoped<IWebhookHandler, IssueCommentWebhookHandler>();
    builder.Services.AddScoped<IWebhookHandler, PullRequestReviewCommentWebhookHandler>();

    // GitHub OAuth config
    builder.Services.Configure<GitHubOAuthOptions>(
        builder.Configuration.GetSection("GitHubOAuth"));

    var webhookSecret = builder.Configuration["WebhookSecret"];
    if (builder.Environment.IsProduction()
        && (string.IsNullOrWhiteSpace(webhookSecret)
            || webhookSecret == "set-me-in-env-vars"
            || webhookSecret == "set-your-github-webhook-secret-here"))
    {
        throw new InvalidOperationException("WebhookSecret must be configured in production.");
    }

    // Session JWT config + token issuance
    var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
    jwtOptions.Validate();
    var jwtSecret = jwtOptions.Secret;
    builder.Services.Configure<JwtOptions>(options => builder.Configuration.GetSection("Jwt").Bind(options));
    builder.Services.AddSingleton<JwtTokenService>();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };

            // SignalR WebSocket clients cannot set Authorization headers;
            // accept access_token in the query string only for the hub route.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var isPunishmentHubRequest = context.HttpContext.Request.Path
                        .StartsWithSegments("/hub/punishment");
                    var accessToken = context.Request.Query["access_token"];
                    if (isPunishmentHubRequest && !string.IsNullOrEmpty(accessToken))
                        context.Token = accessToken;
                    return Task.CompletedTask;
                }
            };
        });

    // JSON serialization (used by Minimal API results + body binding)
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
    });

    // OpenAPI / Swagger
    builder.Services.AddOpenApi();

    // Trust forwarded scheme information only from the local reverse proxy.
    // This lets production issue security headers correctly behind nginx.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Add(IPAddress.Loopback);
    });

    // Rate limiting
    var rateLimitOptions = builder.Configuration.GetSection("RateLimiting").Get<RateLimitOptions>()
        ?? new RateLimitOptions();
    rateLimitOptions.Validate();

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, _) =>
        {
            context.HttpContext.Response.Headers.RetryAfter = rateLimitOptions.RetryAfterSeconds.ToString();
            return ValueTask.CompletedTask;
        };

        options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
            GetClientKey(context), _ => ToLimiterOptions(rateLimitOptions.Api)));

        options.AddPolicy("oauth", context => RateLimitPartition.GetFixedWindowLimiter(
            GetClientKey(context), _ => ToLimiterOptions(rateLimitOptions.Oauth)));

        options.AddPolicy("action", context => RateLimitPartition.GetFixedWindowLimiter(
            GetClientKey(context), _ => ToLimiterOptions(rateLimitOptions.Action)));

        options.AddPolicy("webhook", context => RateLimitPartition.GetFixedWindowLimiter(
            GetClientKey(context), _ => ToLimiterOptions(rateLimitOptions.Webhook)));
    });

    // CORS: closed by default. The native macOS app and GitHub webhook POSTs
    // send no Origin header, so only explicitly configured browser origins get
    // cross-origin access. Allowlist via Cors:AllowedOrigins (empty = deny all).
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? [];
    if (corsOrigins.Length > 0)
    {
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod());

            options.AddPolicy("SignalR", policy =>
                policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
        });
    }

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseForwardedHeaders();
        app.UseHsts();
    }

    // Keep these headers on API, health and SignalR responses without
    // constraining the Scalar/OpenAPI HTML application.
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api")
            || context.Request.Path.StartsWithSegments("/hub")
            || context.Request.Path.StartsWithSegments("/health"))
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        }

        await next();
    });

    // Auto-migrate database
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ApplyMigrations(db);

        var credentialMigration = scope.ServiceProvider.GetRequiredService<GitHubCredentialMigrationService>();
        await credentialMigration.MigrateAsync();

        var cleanup = scope.ServiceProvider.GetServices<IHostedService>()
            .OfType<WorkflowCleanupService>()
            .Single();
        await cleanup.RunOnceAsync();
    }

    if (corsOrigins.Length > 0)
    {
        app.UseCors("SignalR");
    }
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // Liveness must not depend on external services.
    app.MapGet("/health/live", () => Results.Ok(new { status = "alive", timestamp = DateTime.UtcNow }));

    // Readiness reports a failure status when the database cannot be reached.
    app.MapGet("/health/ready", async (AppDbContext db) =>
    {
        try
        {
            var canConnect = await db.Database.CanConnectAsync();
            return canConnect
                ? Results.Ok(new
                {
                    status = "healthy",
                    database = true,
                    timestamp = DateTime.UtcNow
                })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    });

    app.MapGet("/health", async (AppDbContext db) =>
    {
        try
        {
            var canConnect = await db.Database.CanConnectAsync();
            return Results.Json(new
            {
                status = canConnect ? "healthy" : "degraded",
                database = canConnect,
                timestamp = DateTime.UtcNow
            }, statusCode: canConnect ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception)
        {
            return Results.Json(new
            {
                status = "unhealthy",
                database = false,
                timestamp = DateTime.UtcNow
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    });

    app.MapOpenApi();
    app.MapScalarApiReference();

    app.MapHub<PunishmentHub>("/hub/punishment").RequireAuthorization().RequireRateLimiting("api");
    app.MapApiEndpoints();

    await app.RunAsync();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
void ApplyMigrations(AppDbContext db)
{
    // Tests use SQLite in-memory with EnsureCreated(); create schema from model.
    if (string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        db.Database.EnsureCreated();
        return;
    }
    var migrations = db.Database.GetMigrations().ToList();
    if (migrations.Count == 0) return;
    db.Database.Migrate();
}

static string GetClientKey(HttpContext context) =>
    context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } userId
        ? $"user:{userId}"
        : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

static FixedWindowRateLimiterOptions ToLimiterOptions(RateLimitPolicyOptions policy) => new()
{
    PermitLimit = policy.PermitLimit,
    Window = TimeSpan.FromSeconds(policy.WindowSeconds),
    QueueLimit = policy.QueueLimit,
    AutoReplenishment = true
};
