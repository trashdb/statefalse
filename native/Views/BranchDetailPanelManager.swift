import SwiftUI

@MainActor
final class BranchDetailPanelManager: NSObject, NSWindowDelegate {
    static let shared = BranchDetailPanelManager()
    private var panel: NSWindow?

    func show(deps: Dependencies, info: BranchInfo, gitHubId: Int64, onCheckout: (() -> Void)?) {
        if let existing = panel, existing.isVisible {
            existing.makeKeyAndOrderFront(nil)
            return
        }

        let view = BranchDetailView(
            info: info, gitHubId: gitHubId,
            onCheckout: onCheckout,
            onClose: { [weak self] in self?.close() }
        )
        let hostingController = NSHostingController(rootView: view.environment(\.dependencies, deps))
        let w = PanelFactory.makeWindow(size: CGSize(width: 360, height: 340), title: "Branch — \(info.name)")
        w.delegate = self
        w.minSize = CGSize(width: 340, height: 260)
        w.contentViewController = hostingController
        w.makeKeyAndOrderFront(nil)
        panel = w
    }

    func close() {
        panel?.close()
        panel = nil
    }

    func windowWillClose(_ notification: Notification) {
        guard let closingWindow = notification.object as? NSWindow, closingWindow === panel else { return }
        panel = nil
    }
}
