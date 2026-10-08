# Mac notebook

The Mac app starts in the menu bar. It is a native AppKit/SwiftUI application;
the larger dashboard uses the same bundled HTML/TypeScript as Windows in WKWebView.
A self-contained .NET child process runs the common AI polling/parsing code over
private stdio. Hardware readings are native and remain local.

## Build and start

Prerequisites: macOS 15 or newer, Xcode with its command-line tools selected,
.NET 10 SDK, Node.js/npm. Build for the current Mac architecture:

```sh
npx playwright install chromium webkit
./Build-Mac.sh --verify
open "artifacts/mac/AI Dashboard.app"
```

The output includes the .NET runtime. End users do not need an SDK. The bundle is
signed ad hoc for local development, not notarized or packaged as an App Store
release. Build outputs stay ignored. An Intel build is selected on an Intel host;
only Apple Silicon was verified locally. macOS 15 is the declared minimum, while
live verification used macOS 26.5.2/Xcode 26.6 on Mac15,13 (M3).

## Menu and panels

- Default: Codex icon + `34%`, Claude icon + `61%`, then `↻1` (illustrative values). Percentages mean **used**. Transparent monochrome provider glyphs replace letter abbreviations; provider names remain available for accessibility.
- Prefer five-hour windows. If the provider supplies only another main window,
  label it explicitly: Codex icon + `7T 34%` is a weekly bucket, not a five-hour quota.
- `↻1` means one available manual Codex reset; `↻1?` is last-known, unconfirmed
  availability. Missing metadata never implies zero. Usage gets `!` when stale
  or when its reset time has passed.
- The reset indicator remains visible in every menu mode, including icon-only
  and Claude-only. It is text, not an action. No reset can be redeemed here.
- Click the menu item to open the native detail panel. **Anheften** opens a
  separate resizable panel; **Dashboard** opens the larger common dashboard.
- Closing windows leaves the menu item running. **Beenden** exits.
- **Dashboard beim Schließen im Hintergrund behalten** defaults off and is
  available in native settings and web settings under **KI & Bedienung**. Off:
  closing, hiding or minimizing releases the dashboard window, WebKit view and
  native video item/observers. Reopening builds a fresh view using saved settings.
  On: the hidden dashboard and playback are retained. Switching it off also
  releases an already hidden dashboard. Pause alone retains the frame for resume. On monitor
  removal, windows are fitted to an available screen instead of left offscreen.
- The System section includes used/total home-volume storage, used percentage and free space.
- The app, panel, menu bar and shared dashboard use the flat vector branding in
  `assets/branding`; `npm run branding` rebuilds the runtime exports. Provider symbols are native
  vector paths from `assets/providers/*-mark.svg`; `npm run provider-glyphs`
  regenerates their Swift drawing code. They have no colored tile or background.
- Native settings control menu density, pinned-window level and login launch.
  The web settings control the common notebook/stationary profile and appearance.

Provider polling runs every 60 seconds and respects Claude retry delays. Visible
hardware panels update once per second; hardware sampling stops when no panel
or dashboard is visible.
Sleep closes the helper, including its account client, and wake starts it again.
The app does not register a macOS screensaver or a WidgetKit extension.

## Video and fullscreen

Local videos use the native AVFoundation player, including large H.264/AAC MP4
files on mounted drives. The file is read directly without copying it or loading
it entirely into RAM. The toolbar controls play/pause, mute and volume. **Player
bedienen** shows a seek bar; **Dashboard anzeigen** or Escape returns to the cards.
The fullscreen button, the green window button and Control-Command-F toggle the
native fullscreen Space. Window sizing respects the camera safe area.

The bundled web UI is served only from a private loopback URL. The transport
exception applies to localhost; no global HTTP exception is enabled. Local video
is not exposed by the Mac helper's HTTP server.

## Hardware

CPU usage uses Mach system counters; GPU usage reads IOAccelerator statistics.
RAM reports active, wired and compressed pages; it is accompanied by native
memory pressure. Storage reports total/free capacity on the home volume; battery
data comes from IOKit power sources.

Read-only AppleSMC supplies supported temperature zones and measured fan RPM.
The displayed temperatures are the average of readable thermal zones, **not** a
Windows-style CPU package reading or individual CPU/GPU core temperatures. The
actual sensor keys stay in each metric's source. Missing/unknown sensor mappings
remain unavailable. M1/M2/M3/M4 mappings are implemented, but only this M3 was
verified. Some M3 firmware exposes Tp/Tg aliases; both temperature groups were
read successfully here. A fanless MacBook Air has no fan cards, pump or coolant
cards. Other Mac hardware must be checked on its own machine.

No AppleSMC write command, privileged helper or fan-control path is included.

## AI sign-in and context

Codex discovery prefers the installed Codex/ChatGPT app's native client, then a
CLI on PATH/Homebrew. Authentication remains owned by Codex. The helper sends
only initialization and `account/rateLimits/read`; no model request is made.

Claude Code credentials are looked up in Keychain, with its dedicated credentials
file as fallback. Claude Desktop's dedicated OAuth cache is a further fallback;
its macOS v10 OSCrypt payload is decrypted with the existing `Claude Safe Storage`
Keychain entry, locally and in memory. No browser cookies or generic keychain
dump are read, and no access rules are modified. Unsupported/expired credentials,
denied Keychain access, or usage-endpoint changes remain explicitly unavailable.
The OAuth usage endpoint is unofficial, as on Windows.

Codex context is local per-session metadata. **Claude-Code-Kontext verbinden**
in native settings installs the shared status-line collector only if no status
line exists and backs up existing settings before modification. A pre-existing
status line is preserved. New Claude Code sessions supply measurements; ordinary
Claude Desktop chat context is not integrated. Node.js is needed for this optional
collector, not for the installed dashboard itself.

## Verification

- Portable protocol/data checks include a synthetic app-server. They verify the
  exact outbound handshake/read sequence and rejection of reset consumption,
  inference, login and logout methods. Live forbidden-method tests are prohibited.
- Swift tests cover trusted page navigation, reset visibility in all menu modes, stale/expired states,
  zero/missing metadata, honest window labeling and SMC numeric decoding.
- Playwright runs common and notebook UI tests in Chromium and WebKit on macOS.
- `DashboardMac --sensor-probe` prints hardware metadata only; `--sensor-keys`
  enumerates read-only temperature keys for adapter diagnostics.
- Launch with `--demo --demo-panel --demo-dashboard` or `--demo --demo-menu-preview` for synthetic AI UI review
  without provider access. Hardware remains real; do not publish personal captures.
- Windows cross-compilation on macOS verifies compilation, not Windows runtime
  behavior. The existing Windows CI and live checks remain necessary.

Run a native media regression without reading accounts or changing saved settings:

```bash
"artifacts/mac/AI Dashboard.app/Contents/MacOS/AI Dashboard" --demo --verify-media /path/to/fixture.mp4
```

Add `--verify-lifecycle` to the same command to verify close/hide/minimize and
fullscreen-close release, reopen, retained background playback and disabling a
hidden retained dashboard. Weak references check actual view/player deallocation;
verification never persists the background preference.

This explicit mode opens a temporary dashboard, checks advancing playback,
precise seek, pause/resume and fullscreen entry/exit, then quits. The compact report
contains no account metadata, selected file path or private endpoint. A failed
check returns a nonzero exit code. Review with a short synthetic fixture and a
large file; headless browser tests do not verify the native player or Space.

Settings and context collector files use
`~/Library/Application Support/PcAiDashboard`. Native preferences use the
`ch.pezezzle.pc-ai-dashboard` UserDefaults domain. Credentials are never copied
into those files, the repository, diagnostics or screenshots.
