using Statefalse.Domain.Models;

namespace Statefalse.Application;

/// <summary>
/// WorkflowRun persistence + queries (runs list, sync, webhook tracking,
/// superseding and ciStatus lookup).
/// </summary>
public interface IWorkflowRunRepository
{
    Task<WorkflowRun?> FindByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<WorkflowRun?> FindLatestByRunIdAsync(long runId, CancellationToken cancellationToken = default);
    Task<WorkflowRun?> FindByRunIdAndRepoAsync(long runId, string repo, CancellationToken cancellationToken = default);
    Task<WorkflowRun?> FindInProgressByRunIdAsync(long runId, CancellationToken cancellationToken = default);
    Task<WorkflowRun?> FindLatestInProgressByRunIdAsync(long runId, CancellationToken cancellationToken = default);
    Task<bool> AnyInProgressByRunIdAsync(long runId, CancellationToken cancellationToken = default);
    Task<List<string>> GetInProgressReposForUserAsync(long gitHubId, CancellationToken cancellationToken = default);
    Task<List<WorkflowRun>> GetForUserAsync(long gitHubId, int limit, CancellationToken cancellationToken = default);
    Task<List<WorkflowRun>> GetTargetRunsAsync(long gitHubId, int limit, CancellationToken cancellationToken = default);
    Task<List<WorkflowRun>> GetCandidatesAsync(long gitHubId, ICollection<string> repos, int limit, CancellationToken cancellationToken = default);
    Task<List<WorkflowRun>> GetByShasForReposAsync(ICollection<string> repos, CancellationToken cancellationToken = default);
    Task<List<WorkflowRun>> FindSupersededAsync(int excludeId, string repo, string workflowName, string branch, CancellationToken cancellationToken = default);
    Task<List<WorkflowRun>> FindStaleAsync(string repo, string branch, CancellationToken cancellationToken = default);
    Task AddAsync(WorkflowRun workflowRun, CancellationToken cancellationToken = default);
}
