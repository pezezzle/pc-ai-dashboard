# Build and test status

Last verified: **2026-10-08**. Application version: **1.0.3**. Earlier media/audio verification was performed on 2026-10-06.

## Build workflow

From the repository root in PowerShell:

```powershell
.\Build.ps1 -Verify
.\Build.ps1 -Publish -Verify
```

The script restores npm packages, compiles TypeScript, builds .NET, runs the requested checks, and optionally publishes a self-contained Windows x64 folder. Each step stops on failure.

Individual commands:

```powershell
npm ci
npm run build
npm run check
dotnet build PcAiDashboard.slnx -c Release
dotnet run --project tests/PcAiDashboard.Checks -c Release --no-build
npx playwright test
dotnet publish src/PcAiDashboard/PcAiDashboard.csproj -c Release -r win-x64 --self-contained true -o artifacts/app
```

GitHub Actions runs the build, checks generated JavaScript consistency, runs the checks, and uploads a Windows Actions artifact. The artifact is not an installer or a GitHub Release.

## Verification results

| Check | Result |
|---|---|
| Release build | Passed with zero warnings and errors |
| Data and media protocol checks | 44 passed, including dashboard monitor selection/defaults/persistence, six simultaneous long-running video responses plus metadata, inactivity thresholds, disabled timers, tick rollover, reset counts, expiry filtering, credit units, missing balances, and PWM decoding |
| Edge interface tests | 14 passed, including dashboard checkboxes, all-video mode, portrait stretching, screensaver toolbar fit, manual command, timer persistence/validation, hidden saver controls, manual availability/staleness, two quota windows, credits, unclipped larger text, PWM rings, and storage decimals |
| Screensaver | Manual and one-minute inactivity start, live data/background on five full-size monitor windows, mixed DPI/orientations, mouse/keyboard dismissal, return to the normal dashboard, repeated start/dismissal, and app shutdown during screensaver mode verified |
| Screensaver video and selection | Time and decoded frames advance on all five monitors, including portrait, with stretched viewport-filling video; single-monitor dashboard and all-video mode both keep five fullscreen backgrounds |
| Live hardware | CPU, GPU, RAM, fixed-drive storage, OCTO temperatures, pump and four fan groups verified |
| AI usage | Codex and Claude windows verified with valid provider sessions |
| Credits and layout | Live Codex balance, Claude monthly spending with unavailable funded balance, all five PWM channels, two-decimal storage, and unclipped bottom cards verified |
| Manual reset banner | Live available count and expiry verified with the bundled Codex client; no reset was consumed; video continued through another usage poll |
| YouTube | Playback, mute/unmute, pause/resume, and unavailable-video errors verified |
| Large MP4 | 7.4 GB, 7:33:59, H.264/AAC, 1280 × 720; playback and seek to 3:04:27 verified |
| Media ranges | Live read beyond 4 GB; suffix ranges, `HEAD`, `206`, and `416` verified |
| Audio | Volume and mute/unmute verified in the published Windows package |
| Restart | Automatic video playback and saved accent/text colors verified across process restart |

Live testing used Windows 11, an NVIDIA RTX 4090, an OCTO, and Aquasuite. Tests include a short generated WebM and at least thirty seconds of uninterrupted playback of the large MP4. An entire seven-hour playback has not been observed. Other GPU vendors and controller models have not been validated.

## Live integration tools

Tools under `tools/` connect through CDP on `127.0.0.1:9321`. Close the normal app first to avoid the single-instance guard. In a dedicated PowerShell session:

```powershell
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--remote-debugging-port=9321'
Start-Process .\artifacts\app\PcAiDashboard.exe
# Wait until the dashboard loads before connecting.
node tools/inspect-local-video.cjs
node tools/test-local-video.cjs 'C:\path\to\large-video.mp4'
```

Without an argument, the media tool creates a short WebM. It temporarily changes background/audio preferences and restores the original settings afterward. `test-appearance.cjs save` writes test colors and exits; restart the test instance, then run `test-appearance.cjs verify-restore` to verify persistence, restore the original colors, and exit.

`test-webview.cjs` verifies live readings and YouTube. It expects valid Claude/Codex sessions and a gradient background in its test setup. These are local integration checks, not CI tests.

`node tools/test-credits-layout.cjs` verifies credit availability, actual PWM rings, storage formatting, degree scales, card fit, and uninterrupted local video during a subsequent account poll. It expects signed-in providers, an OCTO, and an already playing local background. It does not change settings or persist credentials.

`node tools/test-screensaver.cjs` verifies one native fullscreen window per connected monitor, live readings, hidden controls, silent video copies, global mouse/keyboard dismissal, repeated starts, and the real one-minute inactivity timer. It briefly covers all monitors, nudges the cursor by one pixel, and sends the uncommon F24 key for dismissal without typing text or changing focus. The original timer settings are restored afterward. Keep mouse and keyboard idle during the timer portion; continued input correctly delays automatic start. `test-screensaver-native.ps1` supplies physical monitor/window geometry and input for this local check.

The same tool checks advancing video time and decoded frames on every monitor, full viewport geometry and `object-fit: fill`, a dashboard on only the portrait monitor, and video-only backgrounds everywhere. It restores the original dashboard monitor selection too. The loopback server allows 32 concurrent responses; the previous four slots could be occupied by buffered long-running streams, preventing additional monitor players from starting.

`node tools/test-screensaver-timer.cjs` isolates automatic start with the dashboard selected on the portrait monitor. It uses only the application's own bridge and WebViews, restores preferences afterward, and dismisses the saver through the host bridge without generating OS input.

Close the test app and remove the debug environment variable before normal use:

```powershell
Remove-Item Env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS -ErrorAction SilentlyContinue
.\Start-App.cmd
```

## Diagnostics

Collect a snapshot without opening the window:

```powershell
.\artifacts\app\PcAiDashboard.exe --diagnostics "$env:TEMP\dashboard-diagnostics.json"
```

The JSON contains measurements and usage metadata, not authentication tokens. It can contain personal session labels or device information; review it before sharing. Runtime logs appear in `%LOCALAPPDATA%\PcAiDashboard\app.log` when an event is logged.

## Cleanup

Autostart is disabled by default. If enabled, turn it off in settings to remove the current user's `PcAiDashboard` Run entry.

To remove the Claude Code status line, remove only the entry this app added to `~/.claude/settings.json`. Compare `claude-settings.before-dashboard.json` rather than overwriting later edits. Existing third-party status lines are preserved.
