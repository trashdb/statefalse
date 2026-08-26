import SwiftUI

@MainActor
final class WebhookLogPanelManager {
    static let shared = WebhookLogPanelManager()

    func show(api: ApiClientProtocol) {
        UtilityPanelCoordinator.shared.show(
            id: .webhookLog,
            size: CGSize(width: 560, height: 500),
            title: "Webhook Log",
            content: WebhookLogView(api: api)
        )
    }

    func close() {
        UtilityPanelCoordinator.shared.close(.webhookLog)
    }
}
