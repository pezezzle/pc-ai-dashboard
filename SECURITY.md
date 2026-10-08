# Security

Never include API keys, OAuth tokens, authentication files, WebView2 profiles, private conversation files, or personal screenshots in issues, commits, or Actions logs. Use synthetic data and sanitized logs in reports.

## Credentials and local files

The source and Windows package contain credential lookup code, not the publisher's credentials. The app reads the current user's existing Codex and Claude sessions at runtime. Claude Desktop cache decryption occurs locally; tokens stay in memory for authenticated provider requests.

Do not copy `.codex/auth.json`, `.claude/.credentials.json`, Desktop authentication caches, or the dashboard's application data into the repository. Build outputs, settings, diagnostics, and logs are excluded. Ignore rules do not remove secrets from history; inspect history before publishing.

Settings, WebView2 data, limited logs, and Claude Code usage files stay in `%LOCALAPPDATA%\PcAiDashboard`. Diagnostics exclude authentication tokens but can contain session labels and hardware information. Review them before sharing.

## Network and hardware

The app contacts AI providers for usage and YouTube when selected. Hardware telemetry is not uploaded. Cooling access reads input reports only; it does not change speeds, fan curves, or controller settings.

Codex manual-reset availability is read through the native client's account usage method. Reset IDs are not retained, and the dashboard does not redeem or consume resets. Client installation paths are read from the current user's Windows package registry with CLI fallbacks; no client installation or account configuration is changed.

Local video streaming binds only to `127.0.0.1`, uses an unpredictable route, and serves the selected file. Foreign origins and unknown routes are rejected. The service stops when the app exits.

CDP debugging is disabled during normal startup. Use it only for a dedicated local test process, close that process afterward, and remove its environment variable before normal use.

## Reporting

Report potential security issues privately to the maintainer first. Do not post a real token or account cache in a public issue. Revoke or rotate an accidentally published secret through its provider before sharing sanitized reproduction details.
