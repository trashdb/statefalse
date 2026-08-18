import SwiftUI

/// The Statefalse brand mark, shared by the menu bar and native UI.
struct WaveMark: View {
    var color: Color = .primary
    var lineWidth: CGFloat = 2.6

    var body: some View {
        GeometryReader { proxy in
            Path { path in
                let width = proxy.size.width
                let height = proxy.size.height
                let scale = min(width, height) / 24
                path.move(to: CGPoint(x: 3 * scale, y: 9 * scale))
                path.addCurve(
                    to: CGPoint(x: 10 * scale, y: 9 * scale),
                    control1: CGPoint(x: 5.3 * scale, y: 6.7 * scale),
                    control2: CGPoint(x: 7.7 * scale, y: 6.7 * scale)
                )
                path.addLine(to: CGPoint(x: 14 * scale, y: 13 * scale))
                path.addCurve(
                    to: CGPoint(x: 21 * scale, y: 13 * scale),
                    control1: CGPoint(x: 16.3 * scale, y: 15.3 * scale),
                    control2: CGPoint(x: 18.7 * scale, y: 15.3 * scale)
                )
            }
            .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
        }
        .aspectRatio(1, contentMode: .fit)
        .accessibilityHidden(true)
    }
}
