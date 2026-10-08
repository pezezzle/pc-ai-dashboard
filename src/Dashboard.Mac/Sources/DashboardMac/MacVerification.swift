import AppKit
import WebKit

// Explicit development mode: synthetic providers, read-only media, no saved settings.
@MainActor
enum MacVerification {
    static var exitCode: Int32 = 1
    private static func evaluate(_ web: WKWebView, _ source: String) async -> Any? {
        await withCheckedContinuation { continuation in
            web.evaluateJavaScript(source) { value, _ in continuation.resume(returning: value) }
        }
    }
    private static func sample(_ dashboard: WebDashboard) async -> [String: Any] {
        if dashboard.video.snapshot()["active"] as? Bool == true { return dashboard.video.snapshot() }
        let web = dashboard.webView
        return await evaluate(web, """
        (() => { const v = document.querySelector('#local-video'); return v ? {
            hasSource: !!v.currentSrc, readyState: v.readyState, error: v.error?.code ?? 0,
            time: v.currentTime, duration: Number.isFinite(v.duration) ? v.duration : 0,
            paused: v.paused, networkState: v.networkState, frames: v.getVideoPlaybackQuality?.().totalVideoFrames ?? -1, buffered: Array.from({length:v.buffered.length}, (_,i)=>[v.buffered.start(i),v.buffered.end(i)]), protocol: v.currentSrc ? new URL(v.currentSrc).protocol : ''
        } : {}; })()
        """) as? [String: Any] ?? [:]
    }
    private static func pause(_ seconds: Double) async { try? await Task.sleep(for: .seconds(seconds)) }
    static func run(web dashboard: WebDashboard, window: NSWindow) {
        let web = dashboard.webView
        Task { @MainActor in
            var report: [String: Any] = [:]
            var state: [String: Any] = [:]
            for _ in 0..<40 {
                await pause(0.5); state = await sample(dashboard)
                if (state["time"] as? Double ?? 0) > 0.5 { break }
            }
            report["initial"] = state
            report["backgroundTransparent"] = dashboard.backgroundTransparent
            report["showsVideo"] = state["readyForDisplay"] as? Bool ?? false
            report["pageProtocol"] = web.url?.scheme ?? ""
            let before = state["time"] as? Double ?? 0
            await pause(1.5)
            state = await sample(dashboard)
            report["plays"] = (state["time"] as? Double ?? 0) > before + 0.5
            if let duration = state["duration"] as? Double, duration > 2 {
                _ = await evaluate(web, "const position = document.querySelector('#native-video-position'); position.value = \(duration * 0.65); position.dispatchEvent(new Event('change', {bubbles:true}));")
                await pause(2)
                let seeked = await sample(dashboard)
                report["seeked"] = seeked
                report["seeks"] = abs((seeked["time"] as? Double ?? 0) - duration * 0.65) < 5
                let seekTime = seeked["time"] as? Double ?? 0
                await pause(1.5)
                report["playsAfterSeek"] = ((await sample(dashboard))["time"] as? Double ?? 0) > seekTime + 0.5
            }
            _ = await evaluate(web, "document.querySelector('#play').click()")
            await pause(0.5)
            report["pauses"] = dashboard.video.snapshot()["paused"] as? Bool == true
            _ = await evaluate(web, "document.querySelector('#play').click()")
            await pause(1)
            report["resumes"] = dashboard.video.snapshot()["paused"] as? Bool == false
            _ = await evaluate(web, "document.querySelector('#fullscreen').click()")
            await pause(5)
            report["entersFullscreen"] = window.styleMask.contains(.fullScreen)
            report["windowSize"] = [window.frame.width, window.frame.height]
            report["screenSize"] = window.screen.map { [$0.frame.width, $0.frame.height] } ?? []
            report["fillsScreen"] = window.screen.map { abs(window.frame.width - $0.frame.width) < 2 && abs(window.frame.height - ($0.frame.height - $0.safeAreaInsets.top)) < 2 } ?? false
            _ = await evaluate(web, "document.querySelector('#fullscreen').click()")
            await pause(5)
            report["exitsFullscreen"] = !window.styleMask.contains(.fullScreen)
            let checks = ["plays", "showsVideo", "backgroundTransparent", "pauses", "resumes", "seeks", "playsAfterSeek", "entersFullscreen", "fillsScreen", "exitsFullscreen"]
            exitCode = checks.allSatisfy { report[$0] as? Bool == true } ? 0 : 1
            report["passed"] = exitCode == 0
            if let data = try? JSONSerialization.data(withJSONObject: report, options: [.sortedKeys]), let json = String(data: data, encoding: .utf8) {
                print("MAC_MEDIA_VERIFICATION " + json)
                fflush(stdout)
            }
            NSApplication.shared.terminate(nil)
        }
    }
}
