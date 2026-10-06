# Changelog

Versions refer to source and Windows packages. They do not imply a published GitHub Release.

## Unreleased

- Standardize the English README, public repository badges, setup guides, architecture notes, and security documentation.
- Translate build messages and project description into English.

## 1.0.1 — 2026-10-06

- Replace WebView2 response-stream transport with bounded loopback streaming to avoid native crashes with large MP4 files.
- Support media ranges beyond 4 GB, suffix ranges, and metadata-only `HEAD` requests.
- Add configurable text color for measurements and secondary labels.
- Save colors, card opacity, and dimming immediately and restore them after restart.
- Start background playback automatically after restart.
- Expand verification to 20 data/protocol checks and 5 interface tests, plus live media and color-persistence checks.

## 1.0.0 — 2026-10-06

- Initial Windows WPF/WebView2 dashboard with a proportional 1024 × 600 layout.
- Add read-only OCTO, Aquasuite, Windows CPU/RAM/storage, and NVIDIA telemetry.
- Add Claude and Codex account windows, reset countdowns, and session context measurements.
- Add YouTube and local video backgrounds with playback and audio controls.
- Add monitor selection, fullscreen, tray, optional autostart, and GitHub Actions builds.
