# Platform dashboard rules

- Manual Codex resets are strictly display-only. Never redeem or trigger a reset,
  including during diagnostics or live testing. Never send
  `account/rateLimitResetCredit/consume` or introduce any other redemption path.
- Keep the outbound Codex RPC allow-list in `CodexReadOnlyProtocol` limited to
  initialization and `account/rateLimits/read`. Test forbidden requests with
  synthetic data; never try them against a real account.
- Keep reset IDs out of UI snapshots, settings, logs and fixtures derived from
  real accounts. Preserve missing, zero and stale availability as distinct states.
- Keep common data/polling logic in `src/Dashboard.Core`, shared web assets in
  `ui/web`, Windows integrations in `src/PcAiDashboard`, and native Mac code in
  `src/Dashboard.Mac`. Notebook is a display profile, independent of platform.
- Use English for source comments, checks and documentation; the UI is German.
- Before creating, generating, capturing, editing or relocating app media,
  advertising, promotional material or store listings, read and apply
  `~/.codex/skills/pezezzle-asset-archive/SKILL.md`. Keep runtime assets and required
  test fixtures local; choose a numbered Jupiter destination for production media.
- Preserve the existing Windows build and protocol/UI checks. macOS changes need
  Swift tests and WebKit verification. Do not claim a Windows live test based on a
  cross-compilation performed on macOS.
