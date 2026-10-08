# PC / AI Dashboard · Windows

[![Windows build](https://github.com/pezezzle/pc-ai-dashboard/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/pezezzle/pc-ai-dashboard/actions/workflows/build.yml)
[![Last commit](https://img.shields.io/github/last-commit/pezezzle/pc-ai-dashboard/main)](https://github.com/pezezzle/pc-ai-dashboard/commits/main)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20WebView2-0078D4)](docs/ARCHITECTURE.md)
[![TypeScript 5.9](https://img.shields.io/badge/TypeScript-5.9-3178C6?logo=typescript&logoColor=white)](https://www.typescriptlang.org/)
[![Windows x64](https://img.shields.io/badge/Windows-10%20%2F%2011%20x64-0078D4)](docs/SETUP.md)

Windows dashboard for a small HDMI monitor inside a PC. Displays hardware telemetry, Claude and Codex account usage, credit balances when available, and chat context measurements over a local video or YouTube background. Development version **1.0.3**. The host uses C# / .NET 10 and WPF; the interface uses TypeScript in WebView2. The user interface is German; source comments, tests, build messages, and documentation are English.

**Project status:** The Windows build, all 34 data/protocol checks, and all 10 interface tests pass. A 7.4 GB local MP4 was verified with seeking, audio controls, and automatic playback after restart. Both appearance colors were verified across a full process restart. See [Build and test status](docs/BUILD_AND_TEST_STATUS.md) for the tested environment and limitations.

## Features

- CPU package temperature and total usage; GPU core temperature and usage; physical RAM and fixed-drive storage usage.
- Pump, radiator, and case temperatures with a labeled, configurable degree scale; pump RPM and top, side, bottom, and rear fan RPM with actual OCTO PWM rings.
- Claude and Codex account usage windows, reset countdowns, provider-reported credit balances, and per-session context measurements.
- A prominent Codex banner for banked manual limit resets, including the available count and a known expiry date.
- Larger account/storage text and storage percentages with two decimal places using German number formatting, such as `72,34 %`.
- Looping local MP4/WebM backgrounds and an embedded YouTube player with playback, volume, and mute controls.
- Monitor selection, fullscreen, tray controls, optional Windows autostart, and automatically saved accent/text colors, card opacity, and background dimming.

Hardware access is read-only. The app does not change pump speeds, fan curves, or cooling-controller settings.

## Getting started

The current hardware adapters require an Aqua Computer OCTO, Aquasuite, and an NVIDIA GPU with `nvidia-smi`. Windows CPU, RAM, and drive readings do not depend on the OCTO. Unavailable readings remain visibly unavailable.

1. Install the [development prerequisites](docs/SETUP.md#prerequisites) and configure the [Aquasuite export](docs/SETUP.md#aquasuite-cpu-temperature-export).
2. Build from PowerShell in the repository folder:

   ```powershell
   .\Build.ps1 -Publish -Verify
   ```

3. Run `Start-App.cmd` or `artifacts/app/PcAiDashboard.exe`.
4. Open **Einstellungen** using the gear button to choose the monitor, sensor channels, background, and appearance.

For a desktop icon, run `./Create-DesktopShortcut.ps1` after publishing. It creates **PC AI Dashboard** on your Windows desktop with a custom icon and launches the EXE directly, without a command window. Re-run the script if you move the repository. Keep the EXE and its accompanying files in `artifacts/app`.

The smallest monitor is selected on first startup. The 1024 × 600 layout scales proportionally. **F11** toggles fullscreen; **Esc** switches to windowed mode. The tray menu reopens the dashboard, shows settings, or exits.

## Download

Successful [Windows build runs](https://github.com/pezezzle/pc-ai-dashboard/actions/workflows/build.yml) provide a `pc-ai-dashboard-win-x64` artifact. GitHub requires sign-in to download Actions artifacts. Extract the entire folder and run `PcAiDashboard.exe`; keep all accompanying files together. The package includes the .NET runtime and requires the Microsoft Edge WebView2 Runtime.

There is currently no published GitHub Release or installer. Build artifacts are kept outside Git history.

## AI usage and context

The large quota percentage shows **usage already consumed**: `100%` used and `0%` remaining are consistent. The reset countdown comes from the provider. Account limits refresh every 60 seconds without starting a model request.

When Codex reports banked manual resets, its card shows **1 manueller Reset verfügbar** (or the available count), separate from the automatic reset countdown. Check and redeem the reset yourself in Codex under usage. The dashboard only displays availability; it never consumes a reset. Older clients may omit reset metadata, and outdated values are marked as the last known state. Expiry is shown only when returned by the provider.

The credit row shows Codex credit points or a Claude currency balance when the provider returns it. Claude monthly spending and its cap are shown separately; unused monthly budget is never treated as prepaid balance. When an OAuth response omits the balance, the card says **Nicht abrufbar**. Failed requests retain the last known values with a stale indication; Claude rate limiting temporarily pauses polling.

Context is a measurement for one chat, not an account-wide allowance. Select a session directly in its provider card. Claude Code context becomes available from the next session after status-line integration; normal Claude Desktop chat context is not connected.

The Claude usage integration relies on an unofficial OAuth usage endpoint and may require updates if the provider changes it. See [AI integration details](docs/SETUP.md#ai-integrations).

## Video and appearance

Choose a background in **Einstellungen → Video & Ton**. Local videos automatically restart at the end. The toolbar controls playback, mute, volume, and the player view. Local files stream in small blocks, including files larger than 4 GB.

Under **Anzeige**, **Akzentfarbe** changes highlights and **Schriftfarbe** changes text and measurement colors. Colors, card opacity, and dimming save immediately and survive restart. See [Video and appearance](docs/VIDEO_AND_APPEARANCE.md) for details and YouTube limitations.

Temperature bars default to **0–100 °C**, rather than percentage utilization or a safety threshold. **Temperatur-Skala bis (°C)** changes the upper bound. Pump/fan rings show measured **0–100% PWM**, independently of RPM.

## Privacy and security

Credentials are read from the current user's local provider applications at runtime. They are not embedded in source code or written to dashboard files. Settings, the WebView2 profile, limited logs, and Claude Code usage measurements stay in `%LOCALAPPDATA%\PcAiDashboard`.

Hardware telemetry is not uploaded. YouTube and the AI providers are contacted only for their respective features. Local video streaming listens on loopback only and exposes the selected file through an unpredictable access URL. Read [Security](SECURITY.md) before sharing logs, screenshots, or diagnostics.

## Further documentation

- [Setup, hardware channels, and AI integrations](docs/SETUP.md)
- [Architecture and local data flow](docs/ARCHITECTURE.md)
- [Video, audio, and appearance settings](docs/VIDEO_AND_APPEARANCE.md)
- [Build, testing, diagnostics, and cleanup](docs/BUILD_AND_TEST_STATUS.md)
- [Changelog](CHANGELOG.md)
- [Security](SECURITY.md)

## License

No general open-source license has been granted. Public repository access alone is not a license grant. Personal account credentials, private runtime data, and third-party video files are not part of this repository.
