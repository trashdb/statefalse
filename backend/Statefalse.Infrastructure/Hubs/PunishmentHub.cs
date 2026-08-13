using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Statefalse.Domain.Contracts;
using Statefalse.Domain.Models;
using Statefalse.Infrastructure.Data;

namespace Statefalse.Infrastructure.Hubs;

[Authorize]
public class PunishmentHub : Hub
{
    private readonly AppDbContext _db;

    public PunishmentHub(AppDbContext db)
    {
        _db = db;
    }

    public async Task RegisterConnection(string? username = null)
    {
        var claim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (claim == null || !long.TryParse(claim, out var gitHubId))
            throw new HubException("Missing identity claim.");

        var user = await _db.GitHubUsers
            .FirstOrDefaultAsync(u => u.GitHubId == gitHubId);

        if (user != null)
        {
            user.SignalRConnectionId = Context.ConnectionId;
            user.LastLoginAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(username)) user.GitHubUsername = username;
        }
        else
        {
            _db.GitHubUsers.Add(new GitHubUser
            {
                GitHubId = gitHubId,
                GitHubUsername = username ?? $"user_{gitHubId}",
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow,
                SignalRConnectionId = Context.ConnectionId
            });
        }

        await _db.SaveChangesAsync();
        await Groups.AddToGroupAsync(Context.ConnectionId, gitHubId.ToString());

        var pendingPunishments = await _db.PunishmentEvents
            .Where(e => e.CulpritGitHubId == gitHubId
                && !e.WasNotified
                && e.OccurredAt >= DateTime.UtcNow.AddHours(-24))
            .OrderBy(e => e.OccurredAt)
            .Take(10)
            .ToListAsync();

        foreach (var punishment in pendingPunishments)
        {
            await Clients.Caller.SendAsync("WorkflowRunCompleted", new WorkflowRunCompletedPayload(
                RunId: punishment.RunId,
                Succeeded: false,
                Conclusion: "failure",
                WorkflowName: punishment.WorkflowName,
                Repo: punishment.RepoFullName,
                Actor: punishment.CulpritLogin,
                HtmlUrl: punishment.WorkflowUrl,
                Trigger: null));

            punishment.WasNotified = true;
        }

        if (pendingPunishments.Count > 0)
            await _db.SaveChangesAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var user = await _db.GitHubUsers
            .FirstOrDefaultAsync(u => u.SignalRConnectionId == Context.ConnectionId);

        if (user != null)
        {
            user.SignalRConnectionId = null;
            await _db.SaveChangesAsync();
        }

        await base.OnDisconnectedAsync(exception);
    }
}
