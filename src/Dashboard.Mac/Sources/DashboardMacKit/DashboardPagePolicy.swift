import Foundation

// Only the bundled entry page or the helper's private loopback entry page may
// send native bridge messages. Media and embedded frames never gain this trust.
public struct DashboardPagePolicy {
    public let bundledEntry: URL
    public private(set) var localEntry: URL?
    public init(bundledEntry: URL) { self.bundledEntry = bundledEntry.standardizedFileURL }
    public static func localURL(_ address: String) -> URL? {
        guard let url = URL(string: address), url.scheme == "http", url.host == "localhost",
              let port = url.port, (1...65535).contains(port), url.user == nil, url.password == nil,
              url.query == nil, url.fragment == nil else { return nil }
        let parts = url.path.split(separator: "/")
        guard parts.count == 3, parts[0].count == 48, parts[0].allSatisfy({ $0.isHexDigit }),
              parts[1] == "ui", parts[2] == "index.html" else { return nil }
        return url
    }
    public mutating func select(_ url: URL) { localEntry = url }
    public func allows(_ url: URL) -> Bool {
        if let localEntry { return url == localEntry }
        return url.isFileURL && url.standardizedFileURL == bundledEntry
    }
}
