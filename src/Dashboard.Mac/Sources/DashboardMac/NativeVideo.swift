import AppKit
import AVFoundation

@MainActor
final class NativeVideo: NSView {
    private let player = AVPlayer()
    private let playerLayer = AVPlayerLayer()
    private var path = ""
    private var periodic: Any?
    private var ended: NSObjectProtocol?
    var onPlayback: (([String: Any]) -> Void)?
    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true; layer?.addSublayer(playerLayer)
        playerLayer.player = player; playerLayer.videoGravity = .resizeAspectFill
        isHidden = true
        periodic = player.addPeriodicTimeObserver(forInterval: CMTime(seconds: 0.5, preferredTimescale: 600), queue: .main) { [weak self] _ in
            Task { @MainActor in guard let self else { return }; self.onPlayback?(self.snapshot()) }
        }
        ended = NotificationCenter.default.addObserver(forName: .AVPlayerItemDidPlayToEndTime, object: nil, queue: .main) { [weak self] note in
            let item = note.object as? AVPlayerItem
            Task { @MainActor in
                guard let self, item === self.player.currentItem else { return }
                self.player.seek(to: .zero); self.player.play()
            }
        }
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    override func layout() { super.layout(); playerLayer.frame = bounds }
    func configure(_ settings: [String: Any]) {
        let selected = settings["backgroundMode"] as? String == "local" ? settings["localVideoPath"] as? String ?? "" : ""
        if selected != path {
            player.pause(); path = selected
            player.replaceCurrentItem(with: selected.isEmpty ? nil : AVPlayerItem(url: URL(fileURLWithPath: selected)))
            if !selected.isEmpty { player.play() }
        }
        isHidden = selected.isEmpty
        audio(muted: settings["muted"] as? Bool ?? true, volume: settings["volume"] as? Double ?? 25)
    }
    func audio(muted: Bool, volume: Double) {
        player.isMuted = muted
        player.volume = Float(min(max(volume.isFinite ? volume : 25, 0), 100)) / 100
    }
    func toggle() { if player.rate > 0 { player.pause() } else { player.play() }; onPlayback?(snapshot()) }
    func seek(seconds: Double) {
        guard seconds.isFinite, let duration = player.currentItem?.duration.seconds, duration.isFinite, duration > 0 else { return }
        player.seek(to: CMTime(seconds: min(max(0, seconds), duration), preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)
    }
    func snapshot() -> [String: Any] {
        let duration = player.currentItem?.duration.seconds ?? 0
        let time = player.currentTime().seconds
        return ["type": "playback", "native": true, "active": !path.isEmpty, "paused": player.rate == 0,
                "time": time.isFinite ? time : 0, "duration": duration.isFinite ? duration : 0,
                "readyForDisplay": playerLayer.isReadyForDisplay, "error": player.currentItem?.status == .failed ? 1 : 0]
    }
    func close() {
        onPlayback = nil; path = ""; isHidden = true
        player.pause(); player.replaceCurrentItem(with: nil)
        playerLayer.player = nil
        if let periodic { player.removeTimeObserver(periodic); self.periodic = nil }
        if let ended { NotificationCenter.default.removeObserver(ended); self.ended = nil }
    }
}
