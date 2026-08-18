import SwiftUI

final class SettingsPanelManager {
    static let shared = SettingsPanelManager()
    private var panel: NSPanel?
    private var releaseNotesPanel: NSPanel?
    var api: ApiClientProtocol?

    func show() {
        if panel == nil, let api {
            let hostingController = NSHostingController(rootView: SettingsView(api: api))
            let p = PanelFactory.makePanel(size: CGSize(width: 540, height: 400), title: "Settings")
            p.contentViewController = hostingController
            panel = p
        }
        panel?.makeKeyAndOrderFront(nil)
    }

    func close() {
        panel?.close()
        panel = nil
    }

    func showReleaseNotes(markCurrentVersionAsSeen: Bool = false) {
        if releaseNotesPanel == nil {
            let hostingController = NSHostingController(rootView: ReleaseNotesView())
            let p = PanelFactory.makePanel(size: CGSize(width: 560, height: 620), title: "What's New")
            p.contentViewController = hostingController
            releaseNotesPanel = p
        }
        releaseNotesPanel?.makeKeyAndOrderFront(nil)
        if markCurrentVersionAsSeen {
            ReleaseNotesStore.markCurrentVersionAsSeen()
        }
    }

    func closeReleaseNotes() {
        releaseNotesPanel?.close()
        releaseNotesPanel = nil
    }
}
