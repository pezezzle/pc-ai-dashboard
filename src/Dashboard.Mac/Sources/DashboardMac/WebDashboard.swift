import AppKit
import WebKit
import DashboardMacKit

@MainActor
final class WebDashboard: NSObject, WKScriptMessageHandler, WKNavigationDelegate {
    let webView: WKWebView
    let contentView = NSView()
    let video = NativeVideo(frame: .zero)
    private let model: DashboardModel
    private let root: URL
    private var ready = false
    private var closed = false
    private(set) var backgroundTransparent = false
    private var pagePolicy: DashboardPagePolicy
    var onAction: ((String) -> Void)?
    init(model: DashboardModel) {
        self.model = model
        root = Bundle.main.resourceURL!.appendingPathComponent("Web", isDirectory: true)
        pagePolicy = DashboardPagePolicy(bundledEntry: root.appendingPathComponent("index.html"))
        let configuration = WKWebViewConfiguration()
        // Keep web storage transient so it can be released with the dashboard.
        configuration.websiteDataStore = .nonPersistent()
        configuration.mediaTypesRequiringUserActionForPlayback = []
        let script = """
        (() => {
          const callbacks = [];
          window.dashboardBridge = {
            postMessage: data => window.webkit.messageHandlers.dashboard.postMessage(data),
            addEventListener: (type, callback) => { if (type === 'message') callbacks.push(callback); }
          };
          window.dashboardReceive = data => callbacks.forEach(callback => callback({data}));
        })();
        """
        configuration.userContentController.addUserScript(WKUserScript(source: script, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        webView = WKWebView(frame: .zero, configuration: configuration)
        super.init()
        webView.underPageBackgroundColor = .clear
        // AppKit WebKit still draws an opaque backing surface independently of
        // the page CSS. Guard its macOS background setter before using KVC.
        if webView.responds(to: NSSelectorFromString("_setDrawsBackground:")) {
            webView.setValue(false, forKey: "drawsBackground")
            backgroundTransparent = true
        }
        for view in [video, webView] {
            view.translatesAutoresizingMaskIntoConstraints = false; contentView.addSubview(view)
            NSLayoutConstraint.activate([view.leadingAnchor.constraint(equalTo: contentView.leadingAnchor), view.trailingAnchor.constraint(equalTo: contentView.trailingAnchor), view.topAnchor.constraint(equalTo: contentView.topAnchor), view.bottomAnchor.constraint(equalTo: contentView.bottomAnchor)])
        }
        video.onPlayback = { [weak self] in self?.deliver($0) }
        configuration.userContentController.add(self, name: "dashboard")
        webView.navigationDelegate = self
        // Wait for the helper before loading its page. A queued bundle-file load
        // can otherwise supersede the loopback navigation and leave WebKit blank.
        if model.usesBundledDashboard {
            webView.loadFileURL(root.appendingPathComponent("index.html"), allowingReadAccessTo: root)
        } else { deliver(model.configuration) }
    }
    func deliver(_ message: [String: Any]) {
        guard !closed else { return }
        if message["type"] as? String == "configuration", let address = message["dashboardUrl"] as? String,
           let url = DashboardPagePolicy.localURL(address), pagePolicy.localEntry != url {
            pagePolicy.select(url); ready = false
            webView.load(URLRequest(url: url)); return
        }
        var effective = message
        if effective["type"] as? String == "configuration", var settings = effective["settings"] as? [String: Any] {
            video.configure(settings)
            effective["nativeVideo"] = true
            settings["fullscreen"] = webView.window?.styleMask.contains(.fullScreen) == true
            settings["keepDashboardInBackground"] = model.keepDashboardInBackground
            effective["settings"] = settings
        }
        guard ready, let data = try? JSONSerialization.data(withJSONObject: effective), let json = String(data: data, encoding: .utf8) else { return }
        // JSON is passed as data, never interpolated into executable source.
        webView.callAsyncJavaScript("window.dashboardReceive(message)", arguments: ["message": jsonObject(json)], in: nil, in: .page, completionHandler: nil)
    }
    private func jsonObject(_ json: String) -> Any { (try? JSONSerialization.jsonObject(with: Data(json.utf8))) ?? [:] }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.frameInfo.isMainFrame, let url = message.frameInfo.request.url, pagePolicy.allows(url),
              let payload = message.body as? [String: Any], let type = payload["type"] as? String else { return }
        switch type {
        case "ready": ready = true; deliver(model.configuration); deliver(model.snapshot())
        case "saveSettings", "refresh": model.send(payload)
        case "videoToggle": video.toggle()
        case "videoAudio": if let muted = payload["muted"] as? Bool, let volume = payload["volume"] as? Double { video.audio(muted: muted, volume: volume) }
        case "videoSeek": if let seconds = payload["seconds"] as? Double { video.seek(seconds: seconds) }
        case "pickVideo":
            let picker = NSOpenPanel(); picker.allowedContentTypes = [.mpeg4Movie, .movie]; picker.allowsMultipleSelection = false
            if picker.runModal() == .OK, let file = picker.url, var settings = model.configuration["settings"] as? [String: Any] {
                settings["localVideoPath"] = file.path; settings["backgroundMode"] = "local"
                model.send(["type": "saveSettings", "settings": settings])
            }
        case "openData":
            let folder = URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Library/Application Support/PcAiDashboard")
            try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            NSWorkspace.shared.open(folder)
        case "minimize", "exit", "fullscreen", "windowed", "toggleFullscreen": onAction?(type)
        default: break // No account RPC, reset action, or arbitrary native command.
        }
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        if navigationAction.targetFrame?.isMainFrame == false { decisionHandler(.allow); return }
        guard let url = navigationAction.request.url, pagePolicy.allows(url) else {
            decisionHandler(.cancel); return
        }
        decisionHandler(.allow)
    }
    func close() {
        guard !closed else { return }
        closed = true; ready = false; onAction = nil
        video.close(); webView.stopLoading()
        webView.pauseAllMediaPlayback(completionHandler: nil)
        webView.configuration.userContentController.removeScriptMessageHandler(forName: "dashboard")
        webView.navigationDelegate = nil
        webView.removeFromSuperview(); video.removeFromSuperview()
    }
}
