using Microsoft.EntityFrameworkCore;
using Statefalse.Domain.Models;

namespace Statefalse.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<GitHubUser> GitHubUsers => Set<GitHubUser>();
    public DbSet<PunishmentEvent> PunishmentEvents => Set<PunishmentEvent>();
    public DbSet<CheckSuiteEvent> CheckSuiteEvents => Set<CheckSuiteEvent>();
    public DbSet<PullRequestEvent> PullRequestEvents => Set<PullRequestEvent>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GitHubUser>(entity =>
        {
            entity.HasIndex(u => u.GitHubUsername).IsUnique();
            entity.HasIndex(u => u.GitHubId).IsUnique();
        });

        modelBuilder.Entity<PunishmentEvent>(entity =>
        {
            entity.HasIndex(e => e.OccurredAt);
            entity.HasIndex(e => e.CulpritLogin);
        });

        modelBuilder.Entity<CheckSuiteEvent>(entity =>
        {
            entity.HasIndex(e => e.OccurredAt);
            entity.HasIndex(e => e.PrAuthorLogin);
            entity.HasIndex(e => e.Conclusion);
        });

        modelBuilder.Entity<WorkflowRun>(entity =>
        {
            entity.HasIndex(e => e.GitHubId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.RunId);
        });

        modelBuilder.Entity<PullRequestEvent>(entity =>
        {
            entity.HasIndex(e => e.AuthorLogin);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.PrNumber);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasIndex(n => new { n.RecipientGitHubId, n.CreatedAt });
            entity.HasIndex(n => new { n.RecipientGitHubId, n.IsRead, n.CreatedAt });
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => new { t.GitHubId, t.RevokedAt, t.ExpiresAt });
        });

        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.HasIndex(d => d.DeliveryId).IsUnique();
            entity.HasIndex(d => d.ReceivedAt);
        });
    }
}
