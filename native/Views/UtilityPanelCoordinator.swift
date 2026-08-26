import AppKit
import SwiftUI

enum UtilityPanelID: Hashable {
    case settings
    case releaseNotes
    case workflowHistory
    case notificationHistory
    case webhookLog
}

@MainActor
final class UtilityPanelCoordinator: NSObject, NSWindowDelegate {
    static let shared = UtilityPanelCoordinator()

    private var panels: [UtilityPanelID: NSPanel] = [:]

    func show<Content: View>(
        id: UtilityPanelID,
        size: CGSize,
        title: String,
        content: Content
    ) {
        if let panel = panels[id], panel.isVisible {
            focus(panel)
            return
        }

        let panel = PanelFactory.makeUtilityPanel(size: size, title: title)
        panel.delegate = self
        panel.contentViewController = NSHostingController(rootView: content)
        panels[id] = panel
        focus(panel)
    }

    func close(_ id: UtilityPanelID) {
        panels[id]?.close()
        panels[id] = nil
    }

    func closeAll() {
        panels.values.forEach { $0.close() }
        panels.removeAll()
    }

    private func focus(_ panel: NSPanel) {
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
    }

    func windowWillClose(_ notification: Notification) {
        guard let closingPanel = notification.object as? NSPanel,
              let entry = panels.first(where: { $0.value === closingPanel }) else { return }
        panels[entry.key] = nil
    }
}

