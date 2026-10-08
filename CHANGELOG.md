# Changelog

## 1.1.0 — 2026-10-08

- Replace mute/play toolbar characters with accessible monochrome SVG symbols.
- Default Mac dashboard close/hide/minimize to releasing the video and web view;
  add a persisted opt-in background setting in native and dashboard settings.
- Stop hardware sampling when all Mac panels are hidden, and verify resource
  release/reopen behavior in an isolated native lifecycle regression.

- Fix stalled large local videos on Mac with native AVFoundation playback and
  shared play/pause, audio and seek controls. Preserve selected files/settings.
- Fix Mac fullscreen entry/exit and camera-safe geometry; support Control-Command-F.
- Serve bundled Mac UI assets from a private loopback entry with strict bridge
  trust, and add an account-free native media/fullscreen regression mode.

- Render provider menu symbols as transparent native vector glyphs, using the
  macOS foreground color instead of colored app-icon images.

- Replace CX/CL menu labels with original provider app icons; preserve usage,
  quota-window labels, accessible provider names and read-only reset availability.

- Add used/total storage and a capacity bar to the native System panel.
- Replace the app branding with a light, flat vector design, including native
  Mac app/menu icons, Windows executable/shortcut icons and the shared header.

- Extract portable .NET models, AI polling/parsing, read-only account protocol,
  sensor decoding and local media serving into `Dashboard.Core`; keep Windows
  credential/device integrations in the existing WPF host.
- Add a native macOS menu-bar app with Codex/Claude usage, visible manual-reset
  availability, stale-state markers, pinnable panels and a larger WKWebView dashboard.
- Add a platform-independent notebook profile and move shared HTML/CSS/generated
  JavaScript to `ui/web`; keep the stationary 1024 × 600 display and Windows saver.
- Read Mac CPU/GPU usage, temperature zones, RAM, memory pressure, battery and
  storage without sensor/fan writes; omit absent notebook cooling controllers.
- Read Claude Code Keychain credentials and dedicated Claude Desktop OAuth caches
  on macOS. Respect existing status lines and offer an explicit context integration.
- Forbid manual-reset redemption through an outbound RPC allow-list. Verify it
  with a synthetic server and test menu availability, stale state and missing windows.
- Add Mac packaging/CI and Chromium/WebKit UI checks. Avoid replacing unchanged
  saver-button text during input blur, which cancelled clicks in WebKit.


Versions refer to source and Windows packages. They do not imply a published GitHub Release.

## Unreleased

- Add an in-app screensaver on every connected monitor, with manual start, a saved timer toggle, and a configurable 1–240 minute inactivity wait in the toolbar.
- Share live sensor/account snapshots and the local video server across all screensaver windows; keep copies muted while normal dashboard audio continues.
- Dismiss all screensaver windows together on mouse/keyboard input, monitor changes, or session locking, and restore the normal dashboard's visibility.
- Add saved per-monitor checkboxes for dashboard cards; keep the video screensaver running on every monitor, including an all-video option.
- Stretch local backgrounds to fill portrait monitors and increase video concurrency from four to 32 responses so all monitor players can start while the normal app remains open.
- Expand verification to 44 data/protocol checks and 14 interface tests, plus a local five-monitor integration check that measures video progression on each monitor.

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
