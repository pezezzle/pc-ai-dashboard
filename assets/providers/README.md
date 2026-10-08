# Provider menu symbols

The menu uses transparent monochrome vendor glyphs, not app-icon pictures.
They identify account usage and remain separate from the dashboard's own logo.

- `codex-mark.svg`: `codex_new-f14177b03534.svg` from OpenAI's installed
  ChatGPT/Codex app, `app.asar/webview/assets`, version 26.1002.52244.
- `claude-mark.svg`: the original Claude spark path (`Km`, 32-point geometry)
  from the installed Claude app's `ion-dist/assets/v1/shared-8-DSxoLMgt.js`,
  version 2.26454.2.

`npm run provider-glyphs` compiles the SVG geometry to native NSBezierPath code in
`ProviderGlyph.swift`. The menu draws those shapes without a background or bitmap
asset. Symbols use the same macOS appearance color as the values: light on a dark
menu bar and dark on a light one. Full provider names remain accessible.

`codex.png` and `claude.png` are retained original app icons from the earlier
implementation, for provenance only. The menu does not load them.

The symbols belong to OpenAI and Anthropic respectively. Their use identifies
these services and does not imply endorsement. These small canonical sources and
required runtime code stay local under the asset-archive policy. Builds require
neither installed provider apps nor network/NAS access.
