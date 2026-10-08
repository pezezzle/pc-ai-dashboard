# Sensor and credential protocol references

The project has its own read-only implementations. No third-party binary or full
driver library is bundled.

- AppleSMC read ABI and known thermal-zone keys were checked against
  [Stats SMC](https://github.com/exelban/stats/blob/master/SMC/smc.swift) and
  [Stats sensor definitions](https://github.com/exelban/stats/blob/master/Modules/Sensors/values.swift).
  Stats is MIT-licensed, copyright Serhiy Mytrovtsiy and contributors. Numeric
  protocol layouts and sensor-key identifiers are used as references; fan-write
  methods are not copied or exposed.
- GPU property names were checked against
  [Stats GPU reader](https://github.com/exelban/stats/blob/master/Modules/GPU/reader.swift).
- Claude Desktop's dedicated OAuth-cache field names were checked against
  [Claude Code Usage Monitor](https://github.com/CodeZeno/Claude-Code-Usage-Monitor/blob/main/src/poller/claude_desktop.rs),
  also referenced by the existing Windows integration.
- The macOS v10 cache cryptography was checked against
  [Chromium macOS OSCrypt](https://chromium.googlesource.com/chromium/src/+/refs/tags/130.0.6723.58/components/os_crypt/sync/os_crypt_mac.mm)
  and [Electron safeStorage documentation](https://www.electronjs.org/docs/latest/api/safe-storage).
- Codex account data and optional reset metadata follow the
  [official App Server documentation](https://learn.chatgpt.com/docs/app-server).
  The dashboard deliberately supports only the handshake and usage-read subset.
