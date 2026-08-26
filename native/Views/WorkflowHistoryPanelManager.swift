import SwiftUI

@MainActor
final class WorkflowHistoryPanelManager {
    static let shared = WorkflowHistoryPanelManager()

    func show(signalR: SignalRService, gitHubId: Int64) {
        UtilityPanelCoordinator.shared.show(
            id: .workflowHistory,
            size: CGSize(width: 600, height: 500),
            title: "Workflow History",
            content: WorkflowHistoryView(signalR: signalR, gitHubId: gitHubId)
        )
    }

    func close() {
        UtilityPanelCoordinator.shared.close(.workflowHistory)
    }
}
