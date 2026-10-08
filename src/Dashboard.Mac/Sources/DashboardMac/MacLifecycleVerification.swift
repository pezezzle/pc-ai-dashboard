import AppKit
import WebKit

// Runs only with --verify-media and --verify-lifecycle. Preferences and provider
// accounts stay untouched; weak references verify actual view/player release.
@MainActor
enum MacLifecycleVerification {
    private final class WeakReference<Object: AnyObject> {
        weak var value: Object?
        init(_ value: Object?) { self.value = value }
    }
    private static func pause(_ seconds: Double) async { try? await Task.sleep(for: .seconds(seconds)) }
    private static func waitForPlayback(_ app: DashboardDelegate) async -> Bool {
        for _ in 0..<40 {
            if (app.web?.video.snapshot()["time"] as? Double ?? 0) > 0.5 { return true }
            await pause(0.25)
        }
        return false
    }
    static func run(app: DashboardDelegate) {
        Task { @MainActor in
            var report: [String: Bool] = [:]
            app.model.keepDashboardInBackground = false
            report["initialPlayback"] = await waitForPlayback(app)
            let originalWeb = WeakReference(app.web)
            let originalBrowser = WeakReference(app.web?.webView)
            let originalPlayer = WeakReference(app.web?.video)
            app.dashboard?.performClose(nil)
            await pause(1)
            report["closedViewsReleased"] = originalWeb.value == nil && originalBrowser.value == nil && originalPlayer.value == nil && app.dashboard == nil
            report["hiddenHardwareSamplingStopped"] = !app.model.visibleWindows && !app.model.visiblePanel

            app.showDashboard()
            report["reopensAndPlays"] = await waitForPlayback(app)
            app.model.keepDashboardInBackground = true
            let retainedWeb = WeakReference(app.web)
            let before = app.web?.video.snapshot()["time"] as? Double ?? 0
            app.dashboard?.performClose(nil)
            await pause(1.5)
            report["enabledRetainsViews"] = retainedWeb.value != nil && app.web === retainedWeb.value && app.dashboard?.isVisible == false
            report["enabledContinuesPlayback"] = (app.web?.video.snapshot()["time"] as? Double ?? 0) > before + 0.5
            app.showDashboard()
            report["reopensRetainedView"] = app.web === retainedWeb.value && app.dashboard?.isVisible == true
            app.dashboard?.performClose(nil)
            app.model.keepDashboardInBackground = false
            await pause(1)
            report["disablingReleasesHiddenView"] = retainedWeb.value == nil && app.web == nil && app.dashboard == nil

            app.showDashboard()
            report["playbackBeforeHide"] = await waitForPlayback(app)
            // The web hide command must use the same policy as the close button.
            app.web?.onAction?("minimize")
            await pause(1)
            report["hideReleasesViews"] = app.web == nil && app.dashboard == nil
            app.showDashboard()
            report["playbackBeforeMinimize"] = await waitForPlayback(app)
            app.dashboard?.miniaturize(nil)
            await pause(1)
            report["minimizeReleasesViews"] = app.web == nil && app.dashboard == nil
            app.showDashboard()
            report["playbackBeforeFullscreenClose"] = await waitForPlayback(app)
            app.dashboard?.toggleFullScreen(nil)
            await pause(3)
            report["fullscreenBeforeClose"] = app.dashboard?.styleMask.contains(.fullScreen) == true
            app.dashboard?.performClose(nil)
            await pause(3)
            report["fullscreenCloseReleasesViews"] = app.web == nil && app.dashboard == nil
            report["menuAppStillRunning"] = !app.model.summary.title.isEmpty
            MacVerification.exitCode = report.values.allSatisfy { $0 } ? 0 : 1
            report["passed"] = MacVerification.exitCode == 0
            if let data = try? JSONSerialization.data(withJSONObject: report, options: [.sortedKeys]), let json = String(data: data, encoding: .utf8) {
                print("MAC_LIFECYCLE_VERIFICATION " + json); fflush(stdout)
            }
            NSApplication.shared.terminate(nil)
        }
    }
}
