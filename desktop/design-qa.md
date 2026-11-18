# A visual matching — 0.2 review

Reference: A/light and A/dark discovery 01 and reading 11. Original rasters are 1586×992; compare at a 1280×800 logical canvas. Native traffic lights and the capture indicator are OS-owned.

## Implemented and checked

- Sidebar 210, command bar 68, footer 64; expanding search and outlined navigation/refresh/layout actions.
- Discovery list 432, reading list 335; draggable 280–480. Covers 100×68 / 94×68 with 16 / 12 gap, rounded selected rows and inset separators.
- Reader title 26, avatar 36, follow action, hero 260 / 218, selectable rich text, native media, real game metadata and fixed bottom actions.
- Same sample titles, article and artwork used for comparison. Only artwork regions are read from reference files in the opt-in fixture; UI is built from actual controls.
- Light/dark switching, active-state contrast, 900×640 list/detail navigation, English settings, font import/remove, native share and window reopening checked in an isolated app.
- Data-driven comments, replies, translation, blocked-content, retry, empty, loading and authentication states share the same components. Loading rows use skeletons; blocked navigation does not fetch hidden content until explicit reveal.

## Remaining visual differences

- P2: generated Chinese glyph widths are inconsistent between the two reference pages. Real PingFang text wraps differently; the wide reading page places the quote further below the fold. Readable font metrics and user typography preferences are preserved.
- P3: Fluent book/chat/list icons replace the generated icon shapes; macOS supplies traffic lights, system sheets, native media and sharing.

Structure and component alignment have been reviewed. Pixel-for-pixel identity has **not** passed acceptance. The task's `outputs/heyread-macos-0.2/index.html` provides reference/actual comparison, rather than asserting an unsupported pixel match.

## 0.3.1 · 2026-11-18

At 1280×800 the default 16-point article text, 22-point line height and 10-point paragraph spacing bring the game card and quote into the first screen of the approved Sekiro example. Saved typography overrides are preserved. The workspace gallery at `outputs/a-refinement/index.html` pairs actual screenshots with A pages 02, 07, 11 and 16. The account capture shows the signed-out state. The narrow comment capture uses 900×640.

Generated glyphs, icons, system chrome and the newer product branding still differ. Pages 17–24 have shared layout rules but no individual A rasters for pixel comparison.
