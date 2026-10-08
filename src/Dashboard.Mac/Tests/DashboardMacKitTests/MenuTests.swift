import XCTest
import AppKit
@testable import DashboardMacKit

final class MenuTests: XCTestCase {
    private let now = Date(timeIntervalSince1970: 1_800_000_000)
    private func provider(status: String = "Live", minutes: Int = 300, resets: Int64? = 1, age: TimeInterval = 0, expiry: Int64? = nil) throws -> AiUsage {
        var fixture: [String: Any] = ["name": "Codex", "status": status, "updatedAt": timestamp(now.addingTimeInterval(-age)), "quotas": [["label": minutes == 300 ? "5 Stunden" : "7 Tage", "usedPercent": 34, "windowMinutes": minutes, "resetsAt": Int64(now.timeIntervalSince1970 + 3600)]], "sessions": []]
        if let resets { fixture["manualResets"] = ["availableCount": resets, "nextExpiresAt": expiry as Any? ?? NSNull()] }
        return try JSONDecoder().decode(AiUsage.self, from: JSONSerialization.data(withJSONObject: fixture))
    }
    func testAvailableResetVisibleInEveryMenuMode() throws {
        let value = try provider()
        for mode in MenuMode.allCases {
            XCTAssertTrue(MenuSummary(providers: [value], mode: mode, at: now).title.contains("↻1"))
        }
    }
    func testStaleAndExpiredResetNotPresentedAsConfirmed() throws {
        for value in [try provider(status: "Nicht erreichbar"), try provider(age: 121), try provider(expiry: Int64(now.timeIntervalSince1970 - 1))] {
            XCTAssertTrue(MenuSummary(providers: [value], mode: .both, at: now).title.contains("↻1?"))
        }
    }
    func testZeroAndMissingResetsDoNotInventAvailability() throws {
        for value in [try provider(resets: 0), try provider(resets: nil)] {
            XCTAssertFalse(MenuSummary(providers: [value], mode: .both, at: now).title.contains("↻"))
        }
    }
    func testWeeklyQuotaDoesNotMasqueradeAsFiveHours() throws {
        let title = MenuSummary(providers: [try provider(minutes: 10080)], mode: .both, at: now).title
        XCTAssertTrue(title.contains("Codex 7T 34%")); XCTAssertFalse(title.contains("Codex 34%"))
    }
    func testStaleUsageIsMarkedAndResetCountIsNotClipped() throws {
        let title = MenuSummary(providers: [try provider(resets: 3, age: 121)], mode: .both, at: now).title
        XCTAssertTrue(title.contains("Codex 34%!")); XCTAssertTrue(title.contains("↻3?"))
    }
    func testSMCReadDecodingPreservesZeroAndRejectsUnsupportedTypes() {
        XCTAssertEqual(ReadOnlySMC.decode(type: 0x66706532, bytes: [0, 0]), 0)
        XCTAssertEqual(ReadOnlySMC.decode(type: 0x73703738, bytes: [37, 128]), 37.5)
        XCTAssertEqual(ReadOnlySMC.decode(type: 0x66706532, bytes: [0x0f, 0xa0]), 1000)
        XCTAssertNil(ReadOnlySMC.decode(type: 0x66706532, bytes: [0]))
        XCTAssertNil(ReadOnlySMC.decode(type: 0, bytes: [0, 0]))
    }
    func testFinalMinuteDoesNotClaimTheResetAlreadyHappened() {
        XCTAssertEqual(countdown(Int64(now.timeIntervalSince1970 + 30), at: now), "Reset in 1m")
        XCTAssertEqual(countdown(Int64(now.timeIntervalSince1970), at: now), "Reset fällig")
    }
    func testOriginalProviderIconsReplaceLetterAbbreviations() throws {
        var claude = try provider(resets: nil); claude.name = "Claude"; claude.quotas[0].usedPercent = 61
        let summary = MenuSummary(providers: [try provider(), claude], mode: .both, at: now)
        XCTAssertEqual(summary.items.map(\.provider), [.codex, .claude])
        XCTAssertEqual(summary.items.map(\.value), ["34%", "61%"])
        let rendered = MenuTitleRenderer().render(summary)
        var attachments: [NSTextAttachment] = []
        rendered.enumerateAttribute(.attachment, in: NSRange(location: 0, length: rendered.length)) { value, _, _ in
            if let attachment = value as? NSTextAttachment { attachments.append(attachment) }
        }
        XCTAssertEqual(attachments.count, 2)
        XCTAssertTrue(attachments.allSatisfy { $0.image != nil && $0.bounds.size == NSSize(width: 18, height: 18) })
        XCTAssertTrue(rendered.string.contains("34%")); XCTAssertTrue(rendered.string.contains("61%"))
        XCTAssertTrue(rendered.string.contains("↻1"))
        XCTAssertFalse(rendered.string.contains("CX")); XCTAssertFalse(rendered.string.contains("CL"))
        XCTAssertTrue(summary.title.contains("Codex")); XCTAssertTrue(summary.title.contains("Claude"))
    }
    func testMenuGlyphColorFollowsLightAndDarkAppearance() throws {
        let summary = MenuSummary(providers: [try provider()], mode: .codex, at: now)
        let renderer = MenuTitleRenderer()
        let dark = renderer.render(summary, appearance: NSAppearance(named: .darkAqua))
        let light = renderer.render(summary, appearance: NSAppearance(named: .aqua))
        let darkColor = (dark.attribute(.foregroundColor, at: 1, effectiveRange: nil) as! NSColor).usingColorSpace(.deviceRGB)!
        let lightColor = (light.attribute(.foregroundColor, at: 1, effectiveRange: nil) as! NSColor).usingColorSpace(.deviceRGB)!
        XCTAssertGreaterThan(darkColor.redComponent, 0.7)
        XCTAssertLessThan(lightColor.redComponent, 0.3)
        XCTAssertTrue(dark.string.contains("↻1")); XCTAssertTrue(light.string.contains("↻1"))
    }
    func testProviderGlyphsAreMonochromeWithTransparentCorners() throws {
        for provider in [MenuProvider.codex, .claude] {
            let image = ProviderGlyph.image(provider, color: .white)
            XCTAssertTrue(image.isTemplate)
            let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
            XCTAssertEqual(bitmap.colorAt(x: 0, y: 0)?.alphaComponent, 0)
            XCTAssertEqual(bitmap.colorAt(x: bitmap.pixelsWide-1, y: bitmap.pixelsHigh-1)?.alphaComponent, 0)
            var visiblePixels = 0
            for y in 0..<bitmap.pixelsHigh {
                for x in 0..<bitmap.pixelsWide {
                    guard let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB), color.alphaComponent > 0.5 else { continue }
                    visiblePixels += 1
                    XCTAssertEqual(color.redComponent, 1, accuracy: 0.01)
                    XCTAssertEqual(color.greenComponent, 1, accuracy: 0.01)
                    XCTAssertEqual(color.blueComponent, 1, accuracy: 0.01)
                }
            }
            XCTAssertGreaterThan(visiblePixels, 12)
        }
    }
}
