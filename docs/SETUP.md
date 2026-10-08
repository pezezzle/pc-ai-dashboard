# Setup and integrations

## Prerequisites

| Component | Requirement |
|---|---|
| Operating system | Windows 10 or 11, x64; live verification was performed on Windows 11 |
| Display | A normal HDMI monitor; the 1024 × 600 layout scales proportionally |
| Browser runtime | Microsoft Edge WebView2 Runtime |
| Cooling controller | Aqua Computer OCTO for the current direct USB sensor adapter |
| CPU package temperature | Aquasuite with an active XML export |
| GPU telemetry | NVIDIA GPU and `nvidia-smi` available through the installed driver |
| Codex account limits | Signed-in Codex desktop app or CLI with a Windows-native `codex.exe` |
| Claude account limits | A valid Claude Code or Claude Desktop OAuth session |
| Claude Code context | Node.js and the dashboard's Claude Code status-line integration |
| Source builds | .NET 10 SDK, Node.js, npm; Microsoft Edge for interface tests |

GitHub Actions uses Node.js 24 and the .NET 10 SDK. The published Windows folder includes the .NET runtime. It still needs WebView2 and the applications required for the selected sensor and account integrations.

The current adapters were verified with an OCTO and an NVIDIA RTX 4090. AMD/Intel GPU telemetry and other cooling controllers are not implemented by the direct hardware adapter.

## Aquasuite CPU temperature export

1. In Aquasuite, add an automatic data export named `PC-AI-Dashboard`.
2. Select shared-memory export, enable it, and use a one-second interval.
3. Add the `CPU Package` temperature source with the unit `°C`.
4. Keep Aquasuite running. The dashboard can start it minimized if it is not already open.
5. Use the same shared-memory name under **Einstellungen → Aquasuite & Sensoren**.

An XML export file can be configured instead. The explicit file is authoritative when selected. Exports older than ten seconds are rejected so stale temperatures do not appear live. The dashboard only reads the export.

See [Aquasuite data export, manual section 10.4](https://www.aquacomputer.de/tl_files/aquacomputer/downloads/manuals/aquaero_5_aquaero_6_english.pdf).

## OCTO channel assignment

Settings show channel numbers starting at one. The internal adapter uses zero-based indices.

| Reading | Default UI channel |
|---|---:|
| Pump coolant temperature | Temperature 1 |
| Radiator coolant temperature | Temperature 3 |
| Case temperature | Temperature 4 |
| Side fans | Fan 1 |
| Bottom fans | Fan 2 |
| Rear fans | Fan 3 |
| Top fans | Fan 4 |
| Pump RPM | Fan 5 |

These defaults match the original machine's Aquasuite assignment. Adjust them for another PC. A splitter reports the tachometer signal connected to its channel, not the average RPM of every attached fan.

Each pump/fan ring uses its assigned channel's actual PWM output, decoded in hundredths of a percent. It is not calculated from RPM and is not electrical power consumption. Temperature bars use a separate degree scale, defaulting to 0–100 °C; change **Temperatur-Skala bis (°C)** under **Anzeige** and save. This is a display scale, not a cooling-controller limit or warning threshold.

The adapter reads OCTO HID input reports only. It never sends configuration or speed-control commands. Its layout was checked against the [public liquidctl protocol implementation](https://github.com/liquidctl/liquidctl/blob/main/liquidctl/driver/aquacomputer.py) and live reports. This project contains its own decoding logic.

## Other hardware readings

- CPU total usage comes from Windows system times.
- NVIDIA core temperature and utilization are collected through `nvidia-smi` every two seconds.
- RAM shows physical memory currently used and the total installed memory.
- Storage shows used/free space for mounted fixed volumes, with used percentages formatted to two decimal places using a German decimal comma. Disconnected drives and unmounted volumes are not included.
- Hardware snapshots reach the interface once per second. A source may update less frequently.

## AI integrations

### Codex

The app first checks the current user's registered Codex desktop installation for its bundled native client. Otherwise, it locates the CLI in the standard npm installation or on `PATH`. It starts `codex app-server`, initializes the connection, reads `account/rateLimits/read`, and closes the child process. No inference request is made. Sign-in remains managed by Codex; the dashboard does not install or upgrade either client.

Usage windows and reset times come from the provider response. Missing windows are omitted rather than displayed as zero usage. The app does not reset or increase an account allowance.

The same response supplies `credits.balance`, `hasCredits`, and `unlimited` when available. The balance is displayed as credit points, not currency, with two decimal places. Missing numerical balances remain explicitly unavailable. Account usage and credits refresh every sixty seconds.

Newer clients also return `rateLimitResetCredits`. Its `availableCount` drives the manual-reset banner; the count is authoritative even when detail rows are omitted or truncated. The earliest known expiry among available `codexRateLimits` credits is shown when supplied. Opaque credit identifiers are not retained or sent to the interface. Missing metadata is not treated as zero resets. A saved reset does not imply it can be redeemed in every account state; check Codex's usage controls before redeeming. The dashboard does not call the consume method or redeem anything automatically.

Reset banking is described in the [official June 2026 changelog](https://learn.chatgpt.com/docs/changelog). The local bundled client version 0.160.1 was verified to expose this metadata; the older npm CLI version 0.131.0 omitted it.

Context comes from recent `token_count` measurements in the tails of local `.codex/sessions` files. The app retains usage metadata, not conversation text. The timestamp identifies the last measured input; an idle chat does not receive continuous context measurements.

Reference: [Codex App Server](https://learn.chatgpt.com/docs/app-server).

### Claude

A valid Claude Code OAuth session is preferred. Otherwise, the app checks the dedicated Claude Desktop OAuth cache for the current Windows user. It does not read browser cookies. Desktop cache decryption uses Windows DPAPI and AES-GCM locally. Decrypted tokens remain in memory for the authenticated usage request; they are not saved in dashboard settings or logs.

The OAuth usage endpoint is not a guaranteed public API. It can change or reject an expired session. Cached limits are marked stale, and missing account access remains explicit. Sign in through the provider application if access has expired.

Credit parsing prefers `spend.balance`, while `spend.used` and `spend.limit` describe monthly spending. Money fields are converted using their currency and decimal exponent. Legacy `extra_usage` fields provide a monthly spend/cap fallback only. The app never subtracts spending from the cap to invent a prepaid balance. Some OAuth responses omit the funded balance; the card then displays **Nicht abrufbar** even when monthly spending is known. On HTTP 429, polling respects `Retry-After`, or pauses for three minutes when no delay is provided.

Claude Code context comes from a Node.js status-line integration. On first startup, the dashboard adds it only when no existing `statusLine` is configured. Existing status lines are preserved, and an existing settings file is backed up before modification. The integration writes session identifiers, display labels, context measurements, and limits into the dashboard's local data folder.

Start a new Claude Code session after installation to receive context measurements. Normal Claude Desktop conversation context is not connected.

References: [Claude Code status line](https://code.claude.com/docs/en/statusline) and [the Windows cache format documented in Claude Code Usage Monitor](https://github.com/CodeZeno/Claude-Code-Usage-Monitor/blob/main/src/poller/claude_desktop.rs). This project uses its own .NET cryptography implementation.
