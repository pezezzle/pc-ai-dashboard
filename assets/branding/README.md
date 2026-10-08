# Dashboard branding

`dashboard-icon.svg` is the current canonical app-logo master. It uses a light
flat tile, a navy dashboard frame and three simple usage bars. No gradients,
glow, shadows or generated illustration are part of this design.

`dashboard-mark.svg` is the corresponding monochrome menu symbol. macOS applies
the system's light/dark menu-bar color through a template image.

Run `npm run branding` to generate the required runtime PNG, macOS ICNS, Windows
ICO, menu PNG and shared web SVG. The native bundle and dashboard load those
exports. Normal builds consume the committed exports and never require NAS access.

The older `pc-ai-dashboard.*` and its ImageGen prompt are retained as historical
masters; current app builds and shortcuts use `dashboard-icon.*`.

These small canonical masters and bundled runtime assets stay in this owning
repository under the asset-archive policy. Large promotional media and production
intermediates must use the numbered Jupiter archive instead.
