# Changelog

Versions refer to source and Windows packages. They do not imply a published GitHub Release.

## 1.0.3 — 2026-10-08

- Add a prominent Codex banner for banked manual resets, with the available count and a known expiry date.
- Keep manual availability separate from the automatic quota countdown and mark cached or expired metadata as unverified.
- Prefer the native client bundled with a registered Codex desktop installation, which exposes reset metadata; retain the npm/PATH CLI fallback.
- Read usage only: the dashboard never redeems or consumes a reset.
- Expand verification to 34 data/protocol checks and 10 interface tests.

## 1.0.2 — 2026-10-06

- Display Codex credit balances and Claude funded balances when provided, with monthly spending shown separately from prepaid funds.
- Preserve credit values during connection failures and respect Claude rate-limit retry delays.
- Enlarge account and storage text and reduce the temperature row to improve readability on the small display.
- Label temperature bars in degrees and add a configurable upper scale, defaulting to 100 °C.
- Add actual OCTO PWM rings alongside pump and fan RPM readings.
- Format storage percentages with two decimal places and a German decimal comma.
- Expand verification to 29 data/protocol checks and 7 interface tests.

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
