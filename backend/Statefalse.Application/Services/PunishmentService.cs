using Statefalse.Domain.Contracts;
using Statefalse.Application;

namespace Statefalse.Application;

/// <summary>
/// Punishment (failed workflow) leaderboards + recent event feed.
/// </summary>
public class PunishmentService
{
    private readonly IPunishmentEventRepository _punishments;

    public PunishmentService(IPunishmentEventRepository punishments)
    {
        _punishments = punishments;
    }

    public async Task<ApiResult> GetRecentAsync(int days = 7, int limit = 50)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        var events = await _punishments.GetRecentAsync(since, limit);

        return ApiResult.Ok(events.Select(e => new PunishmentEventDto(
            e.RunId,
            e.CulpritLogin,
            e.RepoFullName,
            e.WorkflowName,
            e.WorkflowUrl,
            e.OccurredAt,
            e.WasNotified)).ToList());
    }

    public async Task<ApiResult> GetSummaryAsync(int days = 7)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        var events = await _punishments.GetRecentAsync(since);

        var topCulprits = events
            .GroupBy(e => e.CulpritLogin)
            .Select(g => new CulpritRankingDto(g.Key, g.Count(), g.Max(e => e.OccurredAt)))
            .OrderByDescending(c => c.Count)
            .Take(5)
            .ToList();

        var topWorkflows = events
            .Where(e => e.WorkflowName != null)
            .GroupBy(e => new { e.WorkflowName, e.RepoFullName })
            .Select(g => new WorkflowRankingDto(g.Key.WorkflowName!, g.Key.RepoFullName, g.Count()))
            .OrderByDescending(w => w.Count)
            .Take(5)
            .ToList();

        var topRepos = events
            .GroupBy(e => e.RepoFullName)
            .Select(g => new RepoRankingDto(g.Key, g.Count()))
            .OrderByDescending(r => r.Count)
            .Take(5)
            .ToList();

        return ApiResult.Ok(new PunishmentSummaryDto
        {
            TopCulprits = topCulprits,
            TopWorkflows = topWorkflows,
            TopRepos = topRepos
        });
    }
}
