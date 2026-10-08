import XCTest
@testable import DashboardMacKit

final class DashboardPageTests: XCTestCase {
    private let entry = "http://localhost:43210/" + String(repeating: "A", count: 48) + "/ui/index.html"
    func testOnlyPrivateHelperEntryIsAccepted() {
        XCTAssertNotNil(DashboardPagePolicy.localURL(entry))
        for invalid in [entry.replacingOccurrences(of: "localhost", with: "example.com"),
                        entry.replacingOccurrences(of: "http:", with: "https:"),
                        entry.replacingOccurrences(of: "43210", with: "0"),
                        entry.replacingOccurrences(of: "localhost", with: "user@localhost"),
                        entry + "?file=private", entry + "#fragment", "http://localhost:43210/ui/index.html"] {
            XCTAssertNil(DashboardPagePolicy.localURL(invalid))
        }
    }
    func testBundleFallbackAndOldHelperCannotKeepNativeBridgeTrust() throws {
        let bundle = URL(fileURLWithPath: "/Applications/Test.app/Contents/Resources/Web/index.html")
        var policy = DashboardPagePolicy(bundledEntry: bundle)
        XCTAssertTrue(policy.allows(bundle))
        XCTAssertFalse(policy.allows(bundle.deletingLastPathComponent().appendingPathComponent("main.js")))
        let first = try XCTUnwrap(DashboardPagePolicy.localURL(entry)); policy.select(first)
        XCTAssertTrue(policy.allows(first)); XCTAssertFalse(policy.allows(bundle))
        XCTAssertFalse(policy.allows(first.deletingLastPathComponent().appendingPathComponent("main.js")))
        let restarted = try XCTUnwrap(DashboardPagePolicy.localURL(entry.replacingOccurrences(of: "43210", with: "43211")))
        policy.select(restarted); XCTAssertFalse(policy.allows(first)); XCTAssertTrue(policy.allows(restarted))
    }
}
