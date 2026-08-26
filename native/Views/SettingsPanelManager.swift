import SwiftUI

@MainActor
final class SettingsPanelManager {
    static let shared = SettingsPanelManager()
    var api: ApiClientProtocol?

    func show() {
        if let api {
            UtilityPanelCoordinator.shared.show(
                id: .settings,
                size: CGSize(width: 540, height: 620),
                title: "Settings",
                content: SettingsView(api: api)
            )
        }
    }

    func close() {
        UtilityPanelCoordinator.shared.close(.settings)
    }

    func showReleaseNotes(markCurrentVersionAsSeen: Bool = false) {
        UtilityPanelCoordinator.shared.show(
            id: .releaseNotes,
            size: CGSize(width: 560, height: 620),
            title: "What's New",
            content: ReleaseNotesView()
        )
        if markCurrentVersionAsSeen {
            ReleaseNotesStore.markCurrentVersionAsSeen()
        }
    }

    func closeReleaseNotes() {
        UtilityPanelCoordinator.shared.close(.releaseNotes)
    }
}
