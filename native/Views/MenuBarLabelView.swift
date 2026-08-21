import AppKit
import SwiftUI

private enum MenuBarIcon {
    static let image: NSImage = {
        // Coordinates use the same top-to-bottom 24-by-24 space as WaveMark.swift and the landing SVG.
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { rect in
            let scale = rect.width / 24
            let height = rect.height
            let path = NSBezierPath()

            path.move(to: NSPoint(x: 3 * scale, y: height - 9 * scale))
            path.curve(
                to: NSPoint(x: 10 * scale, y: height - 9 * scale),
                controlPoint1: NSPoint(x: 5.3 * scale, y: height - 6.7 * scale),
                controlPoint2: NSPoint(x: 7.7 * scale, y: height - 6.7 * scale)
            )
            path.line(to: NSPoint(x: 14 * scale, y: height - 13 * scale))
            path.curve(
                to: NSPoint(x: 21 * scale, y: height - 13 * scale),
                controlPoint1: NSPoint(x: 16.3 * scale, y: height - 15.3 * scale),
                controlPoint2: NSPoint(x: 18.7 * scale, y: height - 15.3 * scale)
            )

            NSColor.black.setStroke()
            path.lineWidth = 2.6
            path.lineCapStyle = .round
            path.stroke()
            return true
        }
        image.isTemplate = true
        return image
    }()
}

struct MenuBarLabelView: View {
    @ObservedObject private var badge = MenuBarBadgeService.shared

    var body: some View {
        Image(nsImage: MenuBarIcon.image)
            .renderingMode(.template)
            .frame(width: 18, height: 18)
            .help(tooltip)
            .accessibilityLabel(tooltip)
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
