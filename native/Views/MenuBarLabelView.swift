import SwiftUI

struct MenuBarLabelView: View {
    @ObservedObject private var badge = MenuBarBadgeService.shared

    var body: some View {
        WaveMark(color: .primary)
            .frame(width: 18, height: 18)
            .help(tooltip)
    }

    private var tooltip: String {
        switch badge.connectionState {
        case .disconnected: return "statefalse — Disconnected"
        case .connected:    return "statefalse — \(badge.activePRCount) active PRs"
        case .hasFailures:  return "statefalse — \(badge.failedPRCount) PR\(badge.failedPRCount == 1 ? "" : "s") failing"
        case .hasRunning:   return "statefalse — \(badge.runningWorkflowCount) workflow\(badge.runningWorkflowCount == 1 ? "" : "s") running"
        }
    }
}
