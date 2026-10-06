# Architecture

The application is a Windows desktop host with a local web interface. It does not require a hosted dashboard backend.

| Component | Responsibility |
|---|---|
| `App.xaml.cs` | Startup, single-instance guard, diagnostics mode |
| `WindowHost.cs` | WPF window, WebView2 bridge, tray, monitor selection, persistence |
| `HardwareService.cs` | Read-only OCTO, Aquasuite, Windows, and NVIDIA telemetry |
| `AiService.cs` | Provider usage polling and local session measurements |
| `ClaudeCredentials.cs` | Current-user OAuth lookup and local cache decryption |
| `LocalVideoServer.cs` | Selected-file loopback streaming and HTTP byte ranges |
| `Models.cs` | Records, validated settings, local file paths, limited logging |
| `ui/main.ts` | Rendering, charts, provider cards, media controls, settings |
| `Web/index.html`, `Web/styles.css` | Proportional layout and appearance |
| `Web/integrations/claude-statusline.cjs` | Claude Code usage metadata collector |

## Interface and host bridge

WebView2 serves the bundled interface from `https://pc-ai-dashboard.local` through a virtual-host folder mapping. TypeScript sends structured messages for settings, file selection, screenshots, and window actions. The host checks the message origin before processing it.

The host pushes snapshots once per second. Provider account queries run every sixty seconds. The interface independently updates reset countdowns and displays last-measured timestamps.

`ui/main.ts` is the maintained source. `npm run build` generates the committed `Web/main.js`. CI fails if rebuilding changes the generated file.

## Local media transport

The service uses a dynamic TCP port bound to `127.0.0.1`. An unpredictable route identifies the selected file; selecting another file rotates the route. No directory listing or arbitrary path lookup is exposed.

It supports `GET`, `HEAD`, preflight requests, and single HTTP byte ranges. Offsets use 64-bit values, including positions beyond 4 GB and suffix ranges. Invalid ranges return `416`. Foreign origins and unknown routes cannot read the selected video.

Up to four requests are handled concurrently, each using a 64 KiB buffer. The video is not loaded into a managed memory buffer. The service stops with the app. This avoids handing a multi-gigabyte response stream through WebView2's COM bridge, which caused the original large-MP4 crash. A similar failure is recorded in the [WebView2 issue tracker](https://github.com/MicrosoftEdge/WebView2Feedback/issues/2577).

## Persistence and local data

The current user's `PcAiDashboard` application data folder contains settings, the WebView2 profile, limited logs, status-line integration, and Claude usage files. Validated settings are written through a temporary file followed by replacement.

Accent color, text color, card opacity, and dimming save on change. Appearance updates do not reposition the window or restart the selected video. Monitor and fullscreen changes are applied separately.

Credentials remain in provider-managed locations. The dashboard neither embeds them in the executable nor writes them to its own settings. See [Security](../SECURITY.md).

## Failure handling

Missing readings stay unavailable. Outdated Aquasuite exports are rejected. Provider connection errors preserve the last known quota with an explicit stale indication. Video errors produce an interface notice without replacing sensor readings.

Normal startup does not expose a CDP debugging port. Test tools require a deliberately started debug instance on loopback.
