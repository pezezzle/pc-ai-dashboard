import AppKit
import Combine
import DashboardMacKit

@MainActor
final class DashboardModel: ObservableObject {
    @Published var providers: [AiUsage] = []
    @Published var hardware = HardwareReading(metrics: [], drives: [], status: "Sensoren werden verbunden …")
    @Published var now = Date()
    @Published var backendStatus = "Verbinden"
    @Published var integrationNotice = ""
    @Published var mode = MenuMode(rawValue: UserDefaults.standard.string(forKey: "menuMode") ?? "both") ?? .both
    @Published var alwaysOnTop = UserDefaults.standard.bool(forKey: "panelAlwaysOnTop")
    @Published var keepDashboardInBackground = UserDefaults.standard.bool(forKey: "keepDashboardInBackground") {
        didSet {
            guard oldValue != keepDashboardInBackground else { return }
            if verificationVideo == nil && !demo {
                UserDefaults.standard.set(keepDashboardInBackground, forKey: "keepDashboardInBackground")
            }
            onBackgroundPreferenceChanged?()
            onConfiguration?(configuration)
        }
    }
    @Published var selectedCodex = UserDefaults.standard.string(forKey: "codexSessionId") ?? ""
    @Published var selectedClaude = UserDefaults.standard.string(forKey: "claudeSessionId") ?? ""
    var configuration: [String: Any] = [:]
    var onSnapshot: (([String: Any]) -> Void)?
    var onConfiguration: (([String: Any]) -> Void)?
    var onPinChanged: (() -> Void)?
    var onBackgroundPreferenceChanged: (() -> Void)?
    var visiblePanel = false
    var visibleWindows = false
    private let demo: Bool
    private let verificationVideo: String?
    private let sensor = MacHardware()
    private var process: Process?
    private var input: FileHandle?
    private var timer: Timer?
    private var sleeping = false
    private var stopping = false
    private var retryAt = Date.distantPast
    private var observers: [NSObjectProtocol] = []

    init(demo: Bool, verificationVideo: String? = nil) {
        self.demo = demo
        self.verificationVideo = verificationVideo
        if demo || verificationVideo != nil { loadDemo() }
        if !demo || verificationVideo != nil { startAgent() }
        hardware = sensor.read()
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        let center = NSWorkspace.shared.notificationCenter
        observers.append(center.addObserver(forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.sleep() }
        })
        observers.append(center.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                self.sleeping = false; self.hardware = self.sensor.read()
                if !self.demo { self.startAgent() }
            }
        })
    }
    var usesBundledDashboard: Bool { demo && verificationVideo == nil }
    var summary: MenuSummary { MenuSummary(providers: providers, mode: mode, at: now) }
    func savePreferences() {
        UserDefaults.standard.set(mode.rawValue, forKey: "menuMode")
        UserDefaults.standard.set(alwaysOnTop, forKey: "panelAlwaysOnTop")
        UserDefaults.standard.set(selectedCodex, forKey: "codexSessionId")
        UserDefaults.standard.set(selectedClaude, forKey: "claudeSessionId")
        if var settings = configuration["settings"] as? [String: Any] {
            settings["codexSessionId"] = selectedCodex; settings["claudeSessionId"] = selectedClaude
            send(["type": "saveSettings", "settings": settings])
        }
        onPinChanged?()
    }
    func tick() {
        guard !sleeping, !stopping else { return }
        now = Date()
        if visiblePanel || visibleWindows { hardware = sensor.read() }
        if !demo && process == nil && now >= retryAt { startAgent() }
        if visibleWindows { onSnapshot?(snapshot()) }
    }
    func snapshot() -> [String: Any] {
        let encoder = JSONEncoder()
        func object<T: Encodable>(_ value: T) -> Any { (try? JSONSerialization.jsonObject(with: encoder.encode(value))) ?? [] }
        return ["type": "snapshot", "data": ["time": timestamp(now), "metrics": object(hardware.metrics), "drives": object(hardware.drives), "ai": object(providers), "hardwareStatus": hardware.status,
                "capabilities": ["platform": "macos", "cooling": false, "fans": hardware.metrics.contains { $0.unit == "RPM" }, "screenSaver": false]]]
    }
    func send(_ message: [String: Any]) {
        // Only local app configuration goes into the helper. No provider RPC pass-through.
        guard let type = message["type"] as? String, ["ready", "refresh", "saveSettings", "installClaudeIntegration"].contains(type) else { return }
        if verificationVideo != nil && !["ready", "refresh"].contains(type) { return }
        var message = message
        if type == "saveSettings", var settings = message["settings"] as? [String: Any] {
            if let keep = settings.removeValue(forKey: "keepDashboardInBackground") as? Bool { keepDashboardInBackground = keep }
            message["settings"] = settings
        }
        if demo && verificationVideo == nil {
            if type == "saveSettings", let settings = message["settings"] as? [String: Any] { configuration["settings"] = settings; onConfiguration?(configuration) }
            return
        }
        guard let data = try? JSONSerialization.data(withJSONObject: message), data.count <= 64_000 else { return }
        do { try input?.write(contentsOf: data + Data([10])) }
        catch { backendStatus = "Verbindung unterbrochen" }
    }
    private func startAgent() {
        guard process == nil, !sleeping, !stopping else { return }
        guard let url = Bundle.main.resourceURL?.appendingPathComponent("Agent/Dashboard.Agent"), FileManager.default.isExecutableFile(atPath: url.path) else {
            backendStatus = "Datenprozess fehlt · Build-Mac.sh ausführen"; retryAt = Date().addingTimeInterval(60); return
        }
        let task = Process(); task.executableURL = url
        if let verificationVideo { task.arguments = ["--verify-media", verificationVideo] }
        let output = Pipe(), inbound = Pipe()
        task.standardOutput = output; task.standardInput = inbound
        task.standardError = FileHandle.nullDevice
        var environment = ProcessInfo.processInfo.environment
        environment["PATH"] = (environment["PATH"] ?? "/usr/bin:/bin") + ":/opt/homebrew/bin:/usr/local/bin:\(NSHomeDirectory())/.local/bin"
        task.environment = environment
        task.terminationHandler = { [weak self] ended in
            Task { @MainActor in
                guard let self, self.process === ended else { return }
                self.process = nil; self.input = nil
                self.backendStatus = self.sleeping ? "Ruhezustand" : "Datenprozess beendet · erneuter Versuch"
                self.retryAt = Date().addingTimeInterval(30)
            }
        }
        do {
            try task.run(); process = task; input = inbound.fileHandleForWriting; backendStatus = "Verbinden"
            DispatchQueue.global(qos: .utility).async { [weak self] in
                var pending = Data()
                while true {
                    let chunk = output.fileHandleForReading.availableData
                    if chunk.isEmpty { break }
                    pending.append(chunk)
                    if pending.count > 2_000_000 { break }
                    while let newline = pending.firstIndex(of: 10) {
                        let line = Data(pending[..<newline]); pending.removeSubrange(...newline)
                        Task { @MainActor in self?.receive(line) }
                    }
                }
            }
        } catch { backendStatus = "Datenprozess konnte nicht starten"; retryAt = Date().addingTimeInterval(30) }
    }
    private func receive(_ data: Data) {
        guard let message = try? JSONSerialization.jsonObject(with: data) as? [String: Any], let type = message["type"] as? String else { return }
        if type == "ai", let values = message["data"], let encoded = try? JSONSerialization.data(withJSONObject: values), let parsed = try? JSONDecoder().decode([AiUsage].self, from: encoded) {
            providers = parsed; backendStatus = "Lokale Verbindung"; onSnapshot?(snapshot())
        } else if type == "configuration" {
            configuration = message; configuration["displays"] = []
            configuration["platform"] = "macos"
            if let settings = message["settings"] as? [String: Any] {
                selectedCodex = settings["codexSessionId"] as? String ?? ""; selectedClaude = settings["claudeSessionId"] as? String ?? ""
            }
            onConfiguration?(configuration)
        } else if type == "notice" { integrationNotice = message["message"] as? String ?? ""; onConfiguration?(message) }
    }
    private func sleep() {
        sleeping = true; stopAgent(); backendStatus = "Ruhezustand"
    }
    private func stopAgent() {
        let task = process; process = nil
        try? input?.close(); input = nil
        // EOF lets the helper cancel provider polling and close its child processes.
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 3) {
            if task?.isRunning == true { task?.terminate() }
        }
    }
    func stop() {
        stopping = true; timer?.invalidate(); stopAgent()
        for observer in observers { NSWorkspace.shared.notificationCenter.removeObserver(observer) }
    }
    private func loadDemo() {
        let reset = Int64(Date().addingTimeInterval(7200).timeIntervalSince1970)
        let week = reset + 86400 * 3
        let fixture: [String: Any] = ["name": "Codex", "status": "Live", "updatedAt": timestamp(), "quotas": [["label": "5 Stunden", "usedPercent": 34, "resetsAt": reset, "windowMinutes": 300], ["label": "7 Tage", "usedPercent": 52, "resetsAt": week, "windowMinutes": 10080]], "sessions": [], "manualResets": ["availableCount": 1, "nextExpiresAt": week]]
        var claude = fixture; claude["name"] = "Claude"; claude.removeValue(forKey: "manualResets")
        claude["quotas"] = [["label": "5 Stunden", "usedPercent": 61, "resetsAt": reset - 3600, "windowMinutes": 300], ["label": "7 Tage", "usedPercent": 28, "resetsAt": week, "windowMinutes": 10080]]
        if let data = try? JSONSerialization.data(withJSONObject: [fixture, claude]), let parsed = try? JSONDecoder().decode([AiUsage].self, from: data) { providers = parsed }
        configuration = ["type": "configuration", "platform": "macos", "displays": [], "settings": ["profile": "notebook", "backgroundMode": "gradient", "youtubeUrl": "", "localVideoPath": "", "muted": true, "volume": 25, "dim": 0.15, "cardOpacity": 0.8, "accent": "#66e7c8", "textColor": "#ecf3f6", "temperatureScaleMax": 100, "fullscreen": false, "displayId": "", "codexSessionId": "", "claudeSessionId": ""]]
        backendStatus = "Demo · synthetische KI-Werte"
    }
}
