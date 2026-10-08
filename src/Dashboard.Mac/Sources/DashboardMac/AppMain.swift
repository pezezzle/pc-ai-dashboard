import AppKit
import SwiftUI
import Combine
import DashboardMacKit

@main
struct DashboardMacApp {
    @MainActor
    static func main() {
        if CommandLine.arguments.contains("--sensor-keys") {
            let smc = ReadOnlySMC()
            for key in smc.availableKeys().filter({ $0.hasPrefix("T") }).sorted() {
                if let value = smc.read(key) { print("\(key): \(value)") }
            }
            return
        }
        if CommandLine.arguments.contains("--sensor-probe") {
            let reader = MacHardware(); _ = reader.read(); Thread.sleep(forTimeInterval: 1)
            if let data = try? JSONEncoder().encode(reader.read()), let json = String(data: data, encoding: .utf8) { print(json) }
            return
        }
        let app = NSApplication.shared
        let delegate = DashboardDelegate(demo: CommandLine.arguments.contains("--demo"))
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        app.run()
        withExtendedLifetime(delegate) {}
    }
}

@MainActor
final class DashboardDelegate: NSObject, NSApplicationDelegate, NSPopoverDelegate, NSWindowDelegate {
    let model: DashboardModel
    private var statusItem: NSStatusItem!
    private let popover = NSPopover()
    private var pinned: NSWindow?
    private(set) var dashboard: NSWindow?
    private var preferences: NSWindow?
    private var menuPreview: NSWindow?
    private(set) var web: WebDashboard?
    private var fullscreenTransition = false
    private var subscriptions = Set<AnyCancellable>()
    private let menuRenderer = MenuTitleRenderer()
    init(demo: Bool) {
        let args = CommandLine.arguments
        let video = args.firstIndex(of: "--verify-media").flatMap { $0 + 1 < args.count ? args[$0 + 1] : nil }
        model = DashboardModel(demo: demo, verificationVideo: video); super.init()
    }
    func applicationDidFinishLaunching(_ notification: Notification) {
        // Never steal a full monitor at launch; the notebook starts in the menu bar.
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.target = self; statusItem.button?.action = #selector(togglePopover)
        statusItem.button?.font = NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .medium)
        popover.behavior = .transient; popover.delegate = self
        popover.contentSize = NSSize(width: 400, height: 650)
        popover.contentViewController = NSHostingController(rootView: panel())
        model.$providers.combineLatest(model.$mode, model.$now).sink { [weak self] _ in
            DispatchQueue.main.async { self?.updateMenu() }
        }.store(in: &subscriptions)
        model.onSnapshot = { [weak self] value in
            if self?.dashboard?.isVisible == true && self?.dashboard?.isMiniaturized == false { self?.web?.deliver(value) }
        }
        model.onConfiguration = { [weak self] value in self?.web?.deliver(value) }
        model.onPinChanged = { [weak self] in self?.pinned?.level = self?.model.alwaysOnTop == true ? .floating : .normal }
        model.onBackgroundPreferenceChanged = { [weak self] in self?.releaseHiddenDashboardIfNeeded() }
        NotificationCenter.default.addObserver(self, selector: #selector(displaysChanged), name: NSApplication.didChangeScreenParametersNotification, object: nil)
        buildMainMenu(); updateMenu()
        if CommandLine.arguments.contains("--demo-panel") { showPinned() }
        if CommandLine.arguments.contains("--demo-settings") { showSettings() }
        if CommandLine.arguments.contains("--demo-dashboard") || CommandLine.arguments.contains("--dashboard") { showDashboard() }
        if CommandLine.arguments.contains("--verify-media") {
            showDashboard()
            if CommandLine.arguments.contains("--verify-lifecycle") { MacLifecycleVerification.run(app: self) }
            else if let web, let dashboard { MacVerification.run(web: web, window: dashboard) }
        }
        if CommandLine.arguments.contains("--demo-menu-preview") {
            let window = makeWindow(title: "AI Dashboard · Menüleisten-Vorschau", size: NSSize(width: 440, height: 80), key: "MenuPreview")
            let button = NSButton(frame: NSRect(x: 20, y: 28, width: 400, height: 24))
            button.isBordered = false
            button.attributedTitle = menuRenderer.render(MenuSummary(providers: model.providers, mode: .both, at: model.now), appearance: window.effectiveAppearance)
            window.contentView?.addSubview(button); menuPreview = window; present(window)
        }
    }
    private func panel() -> DashboardPanel {
        DashboardPanel(model: model, openDashboard: { [weak self] in self?.showDashboard() }, pin: { [weak self] in self?.showPinned() }, settings: { [weak self] in self?.showSettings() })
    }
    private func updateMenu() {
        let summary = model.summary
        statusItem.button?.attributedTitle = menuRenderer.render(summary, appearance: statusItem.button?.effectiveAppearance)
        statusItem.button?.image = nil
        if model.mode == .icon, let url = Bundle.main.resourceURL?.appendingPathComponent("dashboard-mark.png"), let image = NSImage(contentsOf: url) {
            image.size = NSSize(width: 18, height: 18); image.isTemplate = true
            statusItem.button?.image = image
        }
        statusItem.button?.imagePosition = .imageLeading
        statusItem.button?.toolTip = summary.help
        statusItem.button?.setAccessibilityLabel(summary.title + ". " + summary.help)
    }
    @objc private func togglePopover() {
        if popover.isShown { popover.performClose(nil); return }
        guard let button = statusItem.button else { return }
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        model.visiblePanel = true; model.tick()
    }
    func popoverDidClose(_ notification: Notification) { model.visiblePanel = false }
    private func makeWindow(title: String, size: NSSize, key: String) -> NSWindow {
        let window = NSWindow(contentRect: NSRect(origin: .zero, size: size), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = title; window.isReleasedWhenClosed = false; window.delegate = self
        if CommandLine.arguments.contains("--verify-media") { window.center() }
        else {
            window.setFrameAutosaveName(key)
            if !window.setFrameUsingName(key) { window.center() }
        }
        return window
    }
    private func present(_ window: NSWindow) {
        popover.performClose(nil); fitToScreen(window)
        NSApplication.shared.activate(ignoringOtherApps: true); window.makeKeyAndOrderFront(nil)
        model.visibleWindows = true; model.tick()
    }
    func showPinned() {
        if pinned == nil {
            let window = makeWindow(title: "AI Dashboard · Notebook", size: NSSize(width: 400, height: 690), key: "NotebookPanel")
            window.contentViewController = NSHostingController(rootView: panel()); window.minSize = NSSize(width: 380, height: 520)
            window.level = model.alwaysOnTop ? .floating : .normal; pinned = window
        }
        present(pinned!)
    }
    func showDashboard() {
        if dashboard == nil {
            let window = makeWindow(title: "AI Dashboard", size: NSSize(width: 1000, height: 760), key: "DashboardWindow")
            window.collectionBehavior.insert(.fullScreenPrimary)
            let view = WebDashboard(model: model)
            view.onAction = { [weak self] action in
                guard let self else { return }
                switch action {
                case "exit": NSApplication.shared.terminate(nil)
                case "minimize":
                    if self.model.keepDashboardInBackground { self.dashboard?.orderOut(nil); self.updateWindowVisibility() }
                    else { self.dashboard?.performClose(nil) }
                case "toggleFullscreen": self.toggleDashboardFullscreen()
                case "fullscreen": if self.dashboard?.styleMask.contains(.fullScreen) == false { self.toggleDashboardFullscreen() }
                case "windowed": if self.dashboard?.styleMask.contains(.fullScreen) == true { self.toggleDashboardFullscreen() }
                default: break
                }
            }
            window.contentView = view.contentView; window.minSize = NSSize(width: 420, height: 480)
            web = view; dashboard = window
        }
        present(dashboard!)
    }
    func showSettings() {
        if preferences == nil {
            let window = makeWindow(title: "AI Dashboard · Einstellungen", size: NSSize(width: 460, height: 520), key: "DashboardSettings")
            window.setContentSize(NSSize(width: 460, height: 520))
            window.styleMask.remove(.resizable); window.contentViewController = NSHostingController(rootView: SettingsView(model: model)); preferences = window
        }
        present(preferences!)
    }
    func windowWillClose(_ notification: Notification) {
        if notification.object as? NSWindow === dashboard && !model.keepDashboardInBackground { releaseDashboard() }
        DispatchQueue.main.async { [weak self] in self?.updateWindowVisibility() }
    }
    func windowDidMiniaturize(_ notification: Notification) {
        if notification.object as? NSWindow === dashboard && !model.keepDashboardInBackground { dashboard?.close() }
        updateWindowVisibility()
    }
    func windowDidDeminiaturize(_ notification: Notification) { updateWindowVisibility() }
    func windowWillEnterFullScreen(_ notification: Notification) { if notification.object as? NSWindow === dashboard { fullscreenTransition = true } }
    func windowWillExitFullScreen(_ notification: Notification) { if notification.object as? NSWindow === dashboard { fullscreenTransition = true } }
    func windowDidEnterFullScreen(_ notification: Notification) { finishFullscreenTransition(notification) }
    func windowDidExitFullScreen(_ notification: Notification) { finishFullscreenTransition(notification) }
    func windowDidFailToEnterFullScreen(_ window: NSWindow) { fullscreenTransition = false }
    func windowDidFailToExitFullScreen(_ window: NSWindow) { fullscreenTransition = false }
    private func finishFullscreenTransition(_ notification: Notification) {
        guard notification.object as? NSWindow === dashboard else { return }
        fullscreenTransition = false; web?.deliver(model.configuration)
    }
    @objc private func toggleDashboardFullscreen() {
        guard !fullscreenTransition, let dashboard else { return }
        dashboard.toggleFullScreen(nil)
    }
    private func updateWindowVisibility() { model.visibleWindows = [pinned, dashboard].contains { $0?.isVisible == true && $0?.isMiniaturized == false } }
    private func releaseHiddenDashboardIfNeeded() {
        guard !model.keepDashboardInBackground, let dashboard,
              !dashboard.isVisible || dashboard.isMiniaturized else { return }
        dashboard.close(); releaseDashboard()
    }
    private func releaseDashboard() {
        web?.close(); dashboard?.contentView = nil; dashboard?.delegate = nil
        web = nil; dashboard = nil; fullscreenTransition = false
        updateWindowVisibility()
    }
    @objc private func displaysChanged() { for window in [pinned, dashboard, preferences].compactMap({ $0 }) { fitToScreen(window) } }
    private func fitToScreen(_ window: NSWindow) {
        guard !window.styleMask.contains(.fullScreen), !(window === dashboard && fullscreenTransition) else { return }
        if NSScreen.screens.contains(where: { $0.visibleFrame.contains(window.frame) }) { return }
        guard let screen = window.screen ?? NSScreen.main ?? NSScreen.screens.first else { return }
        var frame = window.frame; let area = screen.visibleFrame
        frame.size.width = min(frame.width, area.width); frame.size.height = min(frame.height, area.height)
        frame.origin.x = min(max(frame.minX, area.minX), area.maxX - frame.width)
        frame.origin.y = min(max(frame.minY, area.minY), area.maxY - frame.height)
        window.setFrame(frame, display: true)
    }
    private func buildMainMenu() {
        let menu = NSMenu(); let appItem = NSMenuItem(); menu.addItem(appItem)
        let appMenu = NSMenu(); appItem.submenu = appMenu
        appMenu.addItem(withTitle: "AI Dashboard beenden", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        let editItem = NSMenuItem(); menu.addItem(editItem); let edit = NSMenu(title: "Bearbeiten"); editItem.submenu = edit
        for (title, action, key) in [("Kopieren", "copy:", "c"), ("Einfügen", "paste:", "v"), ("Alles auswählen", "selectAll:", "a")] {
            edit.addItem(withTitle: title, action: Selector(action), keyEquivalent: key)
        }
        let viewItem = NSMenuItem(); menu.addItem(viewItem)
        let viewMenu = NSMenu(title: "Darstellung"); viewItem.submenu = viewMenu
        let fullscreen = viewMenu.addItem(withTitle: "Vollbild umschalten", action: #selector(toggleDashboardFullscreen), keyEquivalent: "f")
        fullscreen.target = self; fullscreen.keyEquivalentModifierMask = [.control, .command]
        NSApplication.shared.mainMenu = menu
    }
    func applicationWillTerminate(_ notification: Notification) {
        model.stop(); web?.close(); NotificationCenter.default.removeObserver(self)
        if CommandLine.arguments.contains("--verify-media") { exit(MacVerification.exitCode) }
    }
}
