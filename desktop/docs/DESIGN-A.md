# Selected design: A · 原生紧凑

Selected by user on 2026-11-16 after requesting fewer repeated design images. Bulk image generation was stopped. A's discovery and reading images define the visual direction; repeated pages are implemented with shared components, rather than generating more near-identical images.

Canvas: 1280×800 logical units; minimum 900×640. Sidebar: 210; header: 68; default list: 432, draggable 280–480. The reading reference uses a 335-wide list. Below 1120 wide, the list and reader occupy the main region in turn; Back/Escape returns to the list. Reader footer: 64. Content inset: 20 left / 18 right. Main title: 26. Body defaults: 17 / 24; paragraph gap: 14. Preferences retain 14–26 body size, line-height ratio 1.2–2.4 and paragraph gap 8–40.

Discovery covers: 100×68 with 16 gap. Compact reader covers: 94×68 with 12 gap. Cards: 92 minimum height, rounded selection and an inset divider. The integrated toolbar has back/forward, expanding search, refresh and layout menu. Reader actions stay anchored below the scrolling document. Original, translation and comparison modes retain media and hierarchy.

Semantic tokens: Surface, Sidebar, Subtle, Line, Ink, Muted, Selection, Accent, AccentFill, OnAccent. Light: #FFFFFF, #F1F3F6, #F6F7F9, #DFE3E9, #151922, #697386, #DFE9FD. Dark: #1C1D20, #24262B, #27292F, #393D46, #F0F1F4, #A1AABB, #263D64. Accent follows the system or user choice; dark accent ink is lightened and filled buttons choose a contrasting foreground. Shared outline, primary, flat, selected and disabled button states are defined in App.axaml. Use Microsoft Fluent System Icons; no FKFinder GPL implementation or private-ABI patch is copied.

Raster references use generated typography that varies between pages. The implementation preserves readable system fonts, text selection, accessible controls and normal ellipsis/wrapping. Native traffic lights and the capture indicator are OS-owned. Reference artwork is cropped only in the opt-in visual fixture; production content always comes from the actual sources.

Page families cover discovery, recommended/followed activities, author/section/topic destinations, scoped search/history, Steam search/news, rich article/translation, comments/replies, image/video, account/folders/cloud/local history, and five settings categories. State coverage uses real loading/error/empty/session/blocked/translation flows, with confirmation dialogs for destructive local actions and text drafts on posting failure.

The reference structure and components are implemented. See ../design-qa.md and the output comparison gallery for measured geometry and remaining font/icon differences. Exact pixel identity is not claimed for generated glyphs or OS chrome.
