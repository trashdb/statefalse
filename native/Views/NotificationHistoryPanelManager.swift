import SwiftUI

final class NotificationHistoryPanelManager {
    static let shared = NotificationHistoryPanelManager()
    private var panel: NSPanel?

    func show(signalR: SignalRService) {
        if panel == nil {
            let hostingController = NSHostingController(
                rootView: NotificationHistoryView(signalR: signalR)
            )
            let p = PanelFactory.makePanel(
                size: CGSize(width: 430, height: 520),
                title: "Notification History"
            )
            p.contentViewController = hostingController
            panel = p
        }
        panel?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func close() {
        panel?.close()
        panel = nil
    }
}
