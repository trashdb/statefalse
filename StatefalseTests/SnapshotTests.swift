import Testing
import SwiftUI
@testable import Statefalse

@MainActor
struct SnapshotTests {
    @Test func emptyState() {
        let view = VStack {
            Image(systemName: "tray")
                .font(.system(size: 32))
                .foregroundStyle(.gray)
            Text("No pull requests")
                .font(.headline)
                .foregroundStyle(.secondary)
        }
        .frame(width: 200, height: 100)
        .background(.black)
        SnapshotTesting.assertSnapshot(of: view, named: "empty_state")
    }

    @Test func badgeCIReady() {
        let view = Text("CI READY")
            .badge("CI READY", color: .green)
            .padding()
            .background(.black)
        SnapshotTesting.assertSnapshot(of: view, named: "badge_ci_ready")
    }

    @Test func badgeCIFail() {
        let view = Text("CI FAIL")
            .badge("CI FAIL", color: .red)
            .padding()
            .background(.black)
        SnapshotTesting.assertSnapshot(of: view, named: "badge_ci_fail")
    }

    @Test func badgeApproved() {
        let view = Text("APPROVED")
            .badge("APPROVED", color: .green)
            .padding()
            .background(.black)
        SnapshotTesting.assertSnapshot(of: view, named: "badge_approved")
    }

}
