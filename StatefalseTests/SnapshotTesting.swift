import Foundation
import Testing
import SwiftUI
import AppKit

/// Lightweight snapshot testing without external dependencies.
/// Renders SwiftUI views to images and compares against reference files.
enum SnapshotTesting {
    /// The `CI` environment variable does NOT propagate through the app host
    /// (TEST_HOST launches the app outside the runner env), so this guard alone
    /// is not reliable. CI excludes this suite via `-skip-testing:StatefalseTests/SnapshotTests`.
    static let isCI = ProcessInfo.processInfo.environment["CI"] != nil

    static var referenceDirectory: URL {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()  // SnapshotTests/
            .deletingLastPathComponent()  // StatefalseTests/
            .appendingPathComponent("ReferenceImages")
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    @MainActor
    static func assertSnapshot<V: View>(of view: V, named name: String) {
        guard !isCI else { return }

        let renderer = ImageRenderer(content: view)
        renderer.scale = 2
        guard let nsImage = renderer.nsImage else {
            Issue.record("Failed to render view '\(name)' to image")
            return
        }
        guard let renderedPNG = pngData(from: nsImage) else {
            Issue.record("Failed to encode '\(name)' to PNG")
            return
        }

        let referenceURL = referenceDirectory
            .appendingPathComponent("\(name).png")

        guard let existingData = try? Data(contentsOf: referenceURL) else {
            try? renderedPNG.write(to: referenceURL)
            Issue.record("First snapshot for '\(name)' saved as reference. Re-run tests to verify.")
            return
        }

        if imagesMatch(existingData, renderedPNG) { return }

        try? renderedPNG.write(to: referenceURL.appendingPathExtension("new"))
        Issue.record("Snapshot mismatch for '\(name)'. New image saved to \(referenceURL.path).new")
    }

    private static func pngData(from image: NSImage) -> Data? {
        guard let tiff = image.tiffRepresentation,
              let rep = NSBitmapImageRep(data: tiff) else { return nil }
        return rep.representation(using: .png, properties: [:])
    }

    private static func imagesMatch(_ referenceData: Data, _ renderedData: Data) -> Bool {
        guard let reference = NSBitmapImageRep(data: referenceData),
              let rendered = NSBitmapImageRep(data: renderedData),
              reference.pixelsWide == rendered.pixelsWide,
              reference.pixelsHigh == rendered.pixelsHigh,
              reference.bitsPerPixel == rendered.bitsPerPixel,
              let referencePixels = reference.bitmapData,
              let renderedPixels = rendered.bitmapData else { return false }

        let bytesPerPixel = max(reference.bitsPerPixel / 8, 1)
        let totalPixels = reference.pixelsWide * reference.pixelsHigh
        var differentPixels = 0
        let channelTolerance: UInt8 = 3

        for y in 0..<reference.pixelsHigh {
            let referenceRow = referencePixels.advanced(by: y * reference.bytesPerRow)
            let renderedRow = renderedPixels.advanced(by: y * rendered.bytesPerRow)
            for x in 0..<reference.pixelsWide {
                let referencePixel = referenceRow.advanced(by: x * bytesPerPixel)
                let renderedPixel = renderedRow.advanced(by: x * bytesPerPixel)
                var differs = false
                for channel in 0..<bytesPerPixel {
                    let a = referencePixel[channel]
                    let b = renderedPixel[channel]
                    if abs(Int(a) - Int(b)) > Int(channelTolerance) {
                        differs = true
                        break
                    }
                }
                if differs { differentPixels += 1 }
            }
        }

        // AppKit text antialiasing may alter a few edge pixels between runs,
        // but layout and content changes affect substantially more pixels.
        return differentPixels <= max(8, totalPixels / 100)
    }
}
