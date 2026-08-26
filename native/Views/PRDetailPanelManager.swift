import SwiftUI

@MainActor
final class PRDetailPanelManager: NSObject, NSWindowDelegate {
    static let shared = PRDetailPanelManager()
    private var panel: NSWindow?

    func show(
        deps: Dependencies,
        pr: PullRequest,
        gitHubId: Int64,
        optimisticDraft: Bool? = nil,
        onDraftChanged: ((Bool) -> Void)? = nil
    ) {
        if let existing = panel, existing.isVisible {
            existing.makeKeyAndOrderFront(nil)
            if existing.title == "Pull Request — #\(pr.prNumber)" { return }
            existing.close()
        }

        let view = PRDetailView(
            pr: pr,
            gitHubId: gitHubId,
            optimisticDraft: optimisticDraft,
            deps: deps,
            onDraftChanged: onDraftChanged,
            onClose: { [weak self] in self?.close() }
        )
        let hostingController = NSHostingController(rootView: view.environment(\.dependencies, deps))
        let w = PanelFactory.makeWindow(size: CGSize(width: 400, height: 560), title: "Pull Request — #\(pr.prNumber)")
        w.delegate = self
        w.minSize = CGSize(width: 360, height: 480)
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
