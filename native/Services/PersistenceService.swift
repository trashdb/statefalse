import Foundation

enum PersistenceService {
    private static var baseURL: URL? {
        guard let dir = try? FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        ) else { return nil }
        return dir
    }

    // --- Workflow history ---

    private static func workflowFileURL(forUser gitHubId: Int64? = nil) -> URL? {
        let filename = gitHubId.map { "workflow_history_\($0).json" } ?? "workflow_history.json"
        return baseURL?.appendingPathComponent(filename)
    }

    static func save(workflows: [WorkflowRun]) {
        guard let url = workflowFileURL() else { return }
        try? (try? JSONEncoder().encode(workflows))?.write(to: url, options: .atomic)
    }

    static func loadWorkflows() -> [WorkflowRun] {
        guard let url = workflowFileURL(),
              let data = try? Data(contentsOf: url),
              let workflows = try? JSONDecoder().decode([WorkflowRun].self, from: data)
        else { return [] }
        return workflows
    }

    static func save(workflows: [WorkflowRun], forUser gitHubId: Int64) {
        guard let url = workflowFileURL(forUser: gitHubId) else { return }
        try? (try? JSONEncoder().encode(workflows))?.write(to: url, options: .atomic)
    }

    static func loadWorkflows(forUser gitHubId: Int64) -> [WorkflowRun] {
        guard let url = workflowFileURL(forUser: gitHubId),
              let data = try? Data(contentsOf: url),
              let workflows = try? JSONDecoder().decode([WorkflowRun].self, from: data)
        else { return [] }
        return workflows
    }

    // --- Active PRs cache ---

    private static var prsFileURL: URL? {
        baseURL?.appendingPathComponent("active_prs_cache.json")
    }

    static func save(prs: [PullRequest]) {
        guard let url = prsFileURL else { return }
        try? JSONEncoder().encode(prs).write(to: url, options: .atomic)
    }

    static func loadPRs() -> [PullRequest] {
        guard let url = prsFileURL,
              let data = try? Data(contentsOf: url),
              let prs = try? JSONDecoder().decode([PullRequest].self, from: data)
        else { return [] }
        return prs
    }
}

// MARK: - Protocol Wrapper for DI

final class LivePersistenceService: PersistenceServiceProtocol {
    func save(workflows: [WorkflowRun]) {
        PersistenceService.save(workflows: workflows)
    }

    func loadWorkflows() -> [WorkflowRun] {
        PersistenceService.loadWorkflows()
    }

    func save(workflows: [WorkflowRun], forUser gitHubId: Int64) {
        PersistenceService.save(workflows: workflows, forUser: gitHubId)
    }

    func loadWorkflows(forUser gitHubId: Int64) -> [WorkflowRun] {
        PersistenceService.loadWorkflows(forUser: gitHubId)
    }

    func save(prs: [PullRequest]) {
        PersistenceService.save(prs: prs)
    }

    func loadPRs() -> [PullRequest] {
        PersistenceService.loadPRs()
    }
}
