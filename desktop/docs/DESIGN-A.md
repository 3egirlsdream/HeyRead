# Selected design: A · 原生紧凑

Selected by user on 2026-11-16 after requesting fewer repeated design images. Bulk image generation was stopped. A's discovery and reading images define the visual direction; repeated pages are implemented with shared components, rather than generating more near-identical images.

Canvas: 1280×800 logical units; minimum 900×640. Sidebar: 200; list: 370 initially, draggable 280–480. Below 1120 wide, navigation switches between a full-width list and reader; Back/Escape returns to the list. Integrated title bar: 48; command row: 64. Body line length capped at 760 with 36 side padding. Main title 26; body 17/30; paragraph gap 20. Body preferences retain 14–26 size, line-height ratio 1.2–2.4 and paragraph spacing 8–40.

Semantic tokens: Surface, Sidebar, Subtle, Line, Ink, Muted, Selection, Accent. Light: #FFFFFF, #F3F4F6, #F7F8FA, #E5E7EB, #1E2025, #747A86, #E9F0FE. Dark: #1C1D20, #24262B, #24262B, #34363D, #F0F1F4, #A1A7B3, #263A5B. Accent follows system or user choice. Buttons 30–32 high with 8 radius; compact groups radius 10. Use Microsoft Fluent System Icons; no copied FKFinder GPL UI implementation or private-ABI patch.

Page families cover discovery, recommended/followed activities, author/section/topic destinations, scoped search/history, Steam search/news, rich article/translation, comments/replies, image/video, account/folders/cloud/local history, and five settings categories. State coverage uses real loading/error/empty/session/blocked/translation flows, with confirmation dialogs for destructive local actions and text drafts on posting failure.

This is an implementation baseline. Exact image-to-window visual acceptance remains a separate user review; the current client is not claimed to have passed 1:1 acceptance.
