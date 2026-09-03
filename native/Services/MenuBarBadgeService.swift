import Combine

struct CachedBranch {
    let name: String
    let repoPath: String
    let repoName: String
    let ticketNumber: String?
}

enum MenuBarConnectionState {
    case disconnected, connected, hasFailures, hasRunning
}

nonisolated extension MenuBarConnectionState: Equatable {}

@MainActor
final class MenuBarBadgeService: ObservableObject {
    static let shared = MenuBarBadgeService()

    @Published var activePRCount = 0
    @Published var failedPRCount = 0
    @Published var runningWorkflowCount = 0
    @Published var draftCount = 0
    @Published var waitingCount = 0
    @Published var reviewCount = 0
    @Published var readyCount = 0
    @Published var mergedCount = 0
    @Published var unreadNotificationCount = 0
    @Published var currentBranches: [CachedBranch] = []
    @Published var connectionState: MenuBarConnectionState = .disconnected


}
