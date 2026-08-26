import SwiftUI

@MainActor
final class NotificationHistoryPanelManager {
    static let shared = NotificationHistoryPanelManager()

    func show(signalR: SignalRService) {
        UtilityPanelCoordinator.shared.show(
            id: .notificationHistory,
            size: CGSize(width: 430, height: 520),
            title: "Notification History",
            content: NotificationHistoryView(signalR: signalR)
        )
    }

    func close() {
        UtilityPanelCoordinator.shared.close(.notificationHistory)
    }
}
