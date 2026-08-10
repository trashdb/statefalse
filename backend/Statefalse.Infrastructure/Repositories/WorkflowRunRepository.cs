using Microsoft.EntityFrameworkCore;
using Statefalse.Application;
using Statefalse.Domain.Models;
using Statefalse.Infrastructure.Data;

namespace Statefalse.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IWorkflowRunRepository"/>.
/// </summary>
public class WorkflowRunRepository : IWorkflowRunRepository
{
    private readonly AppDbContext _db;

    public WorkflowRunRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<WorkflowRun?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<WorkflowRun?> FindLatestByRunIdAsync(long runId, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns
            .Where(w => w.RunId == runId)
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<WorkflowRun?> FindByRunIdAndRepoAsync(long runId, string repo, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns
            .Where(w => w.RunId == runId && w.Repo == repo)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<WorkflowRun?> FindInProgressByRunIdAsync(long runId, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns
            .Where(w => w.RunId == runId && w.Status == "in_progress")
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<WorkflowRun?> FindLatestInProgressByRunIdAsync(long runId, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns
            .Where(w => w.RunId == runId && w.Status == "in_progress")
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> AnyInProgressByRunIdAsync(long runId, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns.AnyAsync(w => w.RunId == runId && w.Status == "in_progress", cancellationToken);

    public Task<List<WorkflowRun>> GetForUserAsync(long gitHubId, int limit, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns
            .Where(w => w.GitHubId == gitHubId && !w.IsIgnored)
            .OrderByDescending(w => w.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<List<WorkflowRun>> GetTargetRunsAsync(long gitHubId, int limit, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns
            .Where(w => w.GitHubId != gitHubId && w.TargetGitHubIds != null && !w.IsIgnored)
            .OrderByDescending(w => w.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<List<WorkflowRun>> GetCandidatesAsync(long gitHubId, ICollection<string> repos, int limit, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns
            .Where(w => !w.IsIgnored
                && w.GitHubId != gitHubId
                && repos.Contains(w.Repo))
            .OrderByDescending(w => w.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<List<WorkflowRun>> GetByShasForReposAsync(ICollection<string> repos, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns
            .Where(w => w.HeadSha != null && repos.Contains(w.Repo))
            .ToListAsync(cancellationToken);

    public Task<List<WorkflowRun>> FindSupersededAsync(int excludeId, string repo, string workflowName, string branch, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns
            .Where(w => w.Id != excludeId && w.Repo == repo && w.WorkflowName == workflowName
                && w.HeadBranch == branch && w.Status == "in_progress")
            .ToListAsync(cancellationToken);

    public Task<List<WorkflowRun>> FindStaleAsync(string repo, string branch, CancellationToken cancellationToken = default)
        => _db.WorkflowRuns
            .Where(w => w.Repo == repo && w.HeadBranch == branch
                && (w.Status == "failure" || w.Status == "in_progress"))
            .ToListAsync(cancellationToken);

    public Task AddAsync(WorkflowRun workflowRun, CancellationToken cancellationToken = default)
    {
        _db.WorkflowRuns.Add(workflowRun);
        return Task.CompletedTask;
    }
}
