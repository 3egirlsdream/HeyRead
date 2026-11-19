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

At 1280×800 the default 16-point article text, 22-point line height and 10-point paragraph spacing bring the game card and quote into the first screen of the approved Sekiro example. Saved typography overrides are preserved. The workspace gallery at `outputs/a-refinement/index.html` pairs actual screenshots with A pages 02, 07, 11 and 16. The initial account capture showed the signed-out state. The narrow comment capture uses 900×640.

Generated glyphs, icons, system chrome and the newer product branding still differ. Pages 17–24 have shared layout rules but no individual A rasters for pixel comparison.

### A geometry pass

Compare A pages 02, 07, 11 and 16 at 1280×800 logical points. The gallery includes both a scaled side-by-side view and a native logical-size switch. The refreshed account capture uses an isolated signed-in sample and the search capture has three local history entries.

| Control | Logical size in the implementation |
| --- | --- |
| Search field, search scope buttons | up to 668×56, at least 77×36 |
| Feed compact cover, wide related-game card | 310×160, 304 wide with a 110 minimum height |
| Account avatar, list item content, logout button | 98×98, 56 high, at least 210×46 |
| Wide reader like, favorite, original | minimum widths 118, 92, 116, all 36 high |
| Reader mode switch | 26-high selected segment within a 36-high outlined group |
| Settings navigation | 42 high in both selected and unselected states |

The layout regression test measures actual Avalonia control bounds in wide and narrow windows. System window chrome, generated game art, glyph metrics and live metadata can differ across screenshots.

## Compact reader · 2026-11-19

The [design board](docs/design/compact-reader.png) shows the same reading pane at two points on the scroll. The macOS implementation uses a 196-point sidebar, a resizable story list with a 344-point default, 48-point toolbars, 68 × 46 feed thumbnails, and 26/19-point root/reply avatars. The filters reflect the available feed channels. The [original app icon](src/HeyRead.Desktop/Assets/app-icon.png) is the source for the sidebar mark and the Dock icon.

All 52 desktop tests passed. They exercise measured button alignment, feed and comment pagination, the comment jump target, draft recovery, and switching from a wide window to a narrow window with a selected article. The packaged arm64 app was checked in a light wide window and a dark narrow window using isolated sample data. Live account posts and Intel execution were outside this validation.

### ImageGen prompts

**Interface.** Generate a pixel-crisp flat front-on design board for an original macOS game news reader. Show two adjacent complete windows at two scroll stages, with a slim pale gray sidebar, a dense white feed and an editorial reader. Use centered outlined controls, 36-point navigation rows, 68 × 46 thumbnails on the left of compact two-line story rows, a Japanese game hero, and a fixed action bar with a comment jump. The second window shows the article ending above inline comments, with 26-point root avatars, 19-point reply avatars, aligned names and dates, and a single light vertical line beside the replies. Use PingFang-like Chinese text, cool grays and restrained blue accents.

**Icon.** Generate one original, flat macOS Dock icon for HeyRead. Take the monochrome geometric mood from the official HeyBox icon. Center a black rounded square on transparent alpha and draw a white folded editorial page with two horizontal cutouts and a small speech tail. Keep it recognizable at 16 pixels. Exclude letters, hexagons, cubes and official branding.
