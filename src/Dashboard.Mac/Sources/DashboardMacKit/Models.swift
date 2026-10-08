import Foundation

public struct Quota: Codable {
    public var label: String
    public var usedPercent: Double
    public var resetsAt: Int64?
    public var windowMinutes: Int?
}
public struct ManualResets: Codable {
    public var availableCount: Int64
    public var nextExpiresAt: Int64?
}
public struct SessionUsage: Codable {
    public var id: String
    public var label: String
    public var usedPercent: Double?
    public var tokens: Int64?
    public var capacity: Int64?
    public var updatedAt: String
}
public struct CreditUsage: Codable {
    public var balance: Double?
    public var unit: String
    public var unlimited: Bool
    public var spent: Double?
    public var limit: Double?
    public var detail: String?
}
public struct AiUsage: Codable {
    public var name: String
    public var status: String
    public var quotas: [Quota]
    public var sessions: [SessionUsage]
    public var updatedAt: String?
    public var detail: String?
    public var credits: CreditUsage?
    public var manualResets: ManualResets?

    public func isFresh(at now: Date) -> Bool {
        guard status == "Live", let updatedAt, let date = parseDate(updatedAt) else { return false }
        return now.timeIntervalSince(date) >= -60 && now.timeIntervalSince(date) <= 120
    }
    // Never silently substitute a weekly bucket for the short-window menu value.
    public var shortQuota: Quota? {
        quotas.first { $0.windowMinutes == 300 && ($0.label == "5 Stunden" || $0.label == "codex · 5 Stunden") }
    }
    public var menuQuota: Quota? {
        shortQuota ?? quotas.filter { !$0.label.contains(" · ") || $0.label.hasPrefix("codex · ") }
            .min { ($0.windowMinutes ?? Int.max) < ($1.windowMinutes ?? Int.max) }
    }
}

public func parseDate(_ string: String) -> Date? {
    let formatter = ISO8601DateFormatter()
    formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    return formatter.date(from: string) ?? ISO8601DateFormatter().date(from: string)
}
public func timestamp(_ date: Date = Date()) -> String {
    let formatter = ISO8601DateFormatter()
    formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    return formatter.string(from: date)
}
public func percent(_ value: Double?) -> String {
    guard let value, value.isFinite else { return "—" }
    return "\(Int(value.rounded()))%"
}
public func countdown(_ epoch: Int64?, at now: Date) -> String {
    guard let epoch else { return "Reset unbekannt" }
    let seconds = Double(epoch) - now.timeIntervalSince1970
    if seconds <= 0 { return "Reset fällig" }
    let minutes = max(1, Int(ceil(seconds / 60)))
    if minutes >= 1440 { return "Reset in \(minutes / 1440)T \(minutes % 1440 / 60)h" }
    if minutes >= 60 { return "Reset in \(minutes / 60)h \(minutes % 60)m" }
    return "Reset in \(minutes)m"
}

public enum MenuMode: String, CaseIterable { case both, icon, codex, claude }
public enum MenuProvider: String, Hashable { case codex = "Codex", claude = "Claude" }
public struct ProviderMenuItem {
    public let provider: MenuProvider
    public let value: String
}
public struct MenuSummary {
    public let title: String
    public let help: String
    public let items: [ProviderMenuItem]
    public let resetIndicator: String
    public init(providers: [AiUsage], mode: MenuMode, at now: Date) {
        let codex = providers.first { $0.name == "Codex" }
        let claude = providers.first { $0.name == "Claude" }
        func label(_ provider: AiUsage?) -> String {
            let quota = provider?.menuQuota
            let stale = quota != nil && provider?.isFresh(at: now) != true
            let expired = quota?.resetsAt.map { Double($0) <= now.timeIntervalSince1970 } ?? false
            var window = ""
            if let quota, quota.windowMinutes != 300 {
                if let minutes = quota.windowMinutes {
                    window = minutes == 10080 ? "7T " : minutes >= 60 && minutes % 60 == 0 ? "\(minutes / 60)h " : "\(minutes)m "
                } else { window = "Limit " }
            }
            return window + percent(quota?.usedPercent) + (stale || expired ? "!" : "")
        }
        var reset = ""
        if let summary = codex?.manualResets, summary.availableCount > 0 {
            let fresh = codex?.isFresh(at: now) == true && (summary.nextExpiresAt.map { Double($0) > now.timeIntervalSince1970 } ?? true)
            reset = "↻\(summary.availableCount)" + (fresh ? "" : "?")
        }
        switch mode {
        case .both: items = [ProviderMenuItem(provider: .codex, value: label(codex)), ProviderMenuItem(provider: .claude, value: label(claude))]
        case .codex: items = [ProviderMenuItem(provider: .codex, value: label(codex))]
        case .claude: items = [ProviderMenuItem(provider: .claude, value: label(claude))]
        case .icon: items = []
        }
        resetIndicator = reset
        let values = items.map { $0.provider.rawValue + " " + $0.value }.joined(separator: " · ")
        title = values + (reset.isEmpty ? "" : (values.isEmpty ? "" : " ") + reset)
        help = "Codex und Claude · Nutzung bereits verbraucht\nStandard: 5 Stunden; andere Zeitfenster sind beschriftet (7T = Woche).\n↻ = verfügbare manuelle Codex-Resets (nur Anzeige)\n! = veraltete Nutzung · ? = letzter bekannter Reset-Stand\nWeitere Zeitfenster im Panel."
    }
}

public struct Metric: Codable {
    public var id: String
    public var label: String
    public var value: Double?
    public var unit: String
    public var source: String
    public var updatedAt: String?
    public init(_ id: String, _ label: String, _ value: Double?, _ unit: String, _ source: String = "macOS") {
        self.id = id; self.label = label; self.value = value; self.unit = unit; self.source = source
        updatedAt = value == nil ? nil : timestamp()
    }
}
public struct DriveUsage: Codable {
    public var name: String
    public var label: String
    public var usedGb: Double
    public var totalGb: Double
    public var usedPercent: Double
}
public struct HardwareReading: Codable {
    public var metrics: [Metric]
    public var drives: [DriveUsage]
    public var status: String
    public init(metrics: [Metric], drives: [DriveUsage], status: String) {
        self.metrics = metrics; self.drives = drives; self.status = status
    }
}
