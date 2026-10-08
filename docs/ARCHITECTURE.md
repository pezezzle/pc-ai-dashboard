# Architecture

The application has two native hosts and a portable .NET core. Both hosts use the
same bundled TypeScript dashboard; there is no hosted dashboard backend.

| Component | Responsibility |
|---|---|
| `src/Dashboard.Core` | Models, AI polling/parsing, read-only Codex RPC policy, settings, sensor protocol parsing, local video HTTP |
| `src/Dashboard.Agent` | Mac-native client/credential discovery, private stdio helper for the Swift app |
| `src/Dashboard.Mac` | Native menu bar, SwiftUI detail/pinned panels, Mac sensors, window lifecycle and WKWebView bridge |
| `src/PcAiDashboard` | Existing WPF/WebView2 host, Windows credential discovery, OCTO/Aquasuite/NVIDIA access, tray and screensaver |
| `ui/main.ts` | Shared rendering, charts, provider cards, media controls and settings |
| `ui/web` | Shared HTML/CSS/generated JavaScript and Claude Code status-line collector |
| `tests/PcAiDashboard.Checks` | Portable data/protocol/media checks, including synthetic-only forbidden-RPC tests |
| `tests/ui` | Common stationary and notebook UI checks; Edge on Windows, Chromium/WebKit on Mac |

The original Windows project/path is retained for existing build scripts. Native
code lives in its respective host; there are no separate long-lived platform
branches. `IAiPlatform` separates executable/credential discovery from provider
polling and parsing. Windows directly references the core; Swift launches the
self-contained .NET helper bundled inside the Mac app and exchanges JSON lines
through inherited pipes. The helper has no listening account API or arbitrary
provider-RPC forwarding method.

`DashboardSettings.Profile` selects stationary or notebook layout independently
of operating system. Defaults remain stationary on Windows; the Mac first-run
default is notebook/windowed. `DashboardSnapshot.Capabilities` describes native
hardware support; existing Windows snapshots remain compatible. The Mac omits
OCTO/pump/coolant cards and displays its battery, memory pressure and available
fan RPM. The compact native panel shares the same AI data contract.

Manual-reset redemption is **strictly forbidden**. `CodexReadOnlyProtocol` has a
fixed outbound allow-list for the handshake and account-limit reading only. UI
bridges accept only local configuration/window actions. Reset counts and expiry
can be shown; opaque reset IDs never enter snapshots, and no consumption action
exists. Tests use a synthetic app-server to verify the exact outbound sequence
and rejection of mutations/inference.

## Interface and host bridge

On Windows, WebView2 serves the bundled interface from `https://pc-ai-dashboard.local` through a virtual-host folder mapping. TypeScript sends structured messages for settings, file selection, screenshots, and window actions. The host checks the message origin before processing it.

Visible dashboards receive snapshots once per second. The Mac helper emits AI data only when changed; hidden native panels sample hardware every thirty seconds and sleep closes the helper. Provider account queries run every sixty seconds. The interface independently updates reset countdowns and displays last-measured timestamps.

`CreditUsage` keeps a funded balance separate from optional monthly spending and limits. Codex numerical strings are parsed with invariant culture. Claude money fields use their declared currency and exponent; absent balances remain null. OCTO fan metrics include independent RPM and PWM readings for each assigned channel. The interface uses PWM percentages for rings and degree-based scales for temperature bars.

`ManualResetUsage` contains only the available count and earliest known expiry. It is read from Codex's rate-limit response, without retaining reset credit IDs or adding a consume action. The banner uses the existing quota region's vertical budget; multiple Codex quota rows remain scrollable. Claude's two-window layout is unchanged. Failed polls retain the last known reset summary, which the interface marks as unverified.

`ui/main.ts` is the maintained source. `npm run build` generates the committed `ui/web/main.js`. CI fails if rebuilding changes the generated file.

On Mac, the helper serves an explicit allow-list of bundled `ui/web` assets at
an unpredictable loopback URL. WKWebView installs a small `dashboardBridge`
adapter. Native messages are accepted only from the main frame at the exact
helper entry URL; helper restarts invalidate the old page's bridge trust.
Standalone synthetic demos use the bundled entry file instead. Snapshots are passed as structured JavaScript
arguments, rather than interpolating JSON into script text. The native menu bar
and panels use SwiftUI, including fresh/stale reset availability. A missing
five-hour bucket can fall back to another main window with its duration visibly
labeled in the menu bar. Window state follows connected screens and never forces
fullscreen on startup. See [Mac details](MAC.md).

## Local media transport

The service uses a dynamic TCP port bound to `127.0.0.1`. An unpredictable route identifies the selected file; selecting another file rotates the route. No directory listing or arbitrary path lookup is exposed.

It supports `GET`, `HEAD`, preflight requests, and single HTTP byte ranges. Offsets use 64-bit values, including positions beyond 4 GB and suffix ranges. Invalid ranges return `416`. Foreign origins and unknown routes cannot read the selected video. Windows uses its virtual HTTPS origin. The Mac helper exposes only bundled UI assets; local Mac video bypasses HTTP.

Up to 32 requests are handled concurrently, each using a 64 KiB buffer. The video is not loaded into a managed memory buffer. The service stops with the app. This avoids handing a multi-gigabyte response stream through WebView2's COM bridge, which caused the original large-MP4 crash. A similar failure is recorded in the [WebView2 issue tracker](https://github.com/MicrosoftEdge/WebView2Feedback/issues/2577).

On Mac, `NativeVideo` reads the selected file through AVFoundation and draws it
under the web dashboard with `AVPlayerLayer`. Play/pause, audio and precise seek
commands remain local; playback updates contain only time, duration and state.
The guarded macOS WebKit background accessor keeps the backing surface transparent;
this accessor is not a public WebKit API and must be rechecked on OS upgrades.
The native fullscreen window declares `fullScreenPrimary`, uses its actual window
state and avoids frame fitting during fullscreen transitions.

The Mac background-retention preference is native UserDefaults state, defaults
false, and is injected into the web settings. It is stripped from shared settings
before sending them to the helper. With retention off, close/hide/minimize removes
native media observers and the AVPlayerItem, pauses web media, removes the bridge
handler and releases the window and views. Web storage is transient. No hardware
sampling or dashboard snapshot encoding runs while all panels/windows are hidden;
read-only AI polling and menu state remain active.

## Persistence and local data

The current user's `PcAiDashboard` application data folder contains settings, limited logs, status-line integration, and Claude usage files. Windows also stores its WebView2 profile there. macOS uses `~/Library/Application Support/PcAiDashboard` and a separate native UserDefaults domain for menu/panel preferences. Validated settings are written through a temporary file followed by replacement.

Accent color, text color, card opacity, and dimming save on change. Appearance updates do not reposition the window or restart the selected video. Monitor and fullscreen changes are applied separately.

Credentials remain in provider-managed locations. The dashboard neither embeds them in the executable nor writes them to its own settings. See [Security](../SECURITY.md).

## Failure handling

Missing readings stay unavailable. Outdated Aquasuite exports are rejected. Provider connection errors preserve the last known quota and credit values with an explicit stale indication. Claude HTTP 429 responses schedule a retry delay instead of continuing once-per-minute requests. Video errors produce an interface notice without replacing sensor readings.

Normal startup does not expose a CDP debugging port. Test tools require a deliberately started debug instance on loopback.
