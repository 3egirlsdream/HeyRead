# macOS validation · 0.2 · 2026-11-16

## Automated and build evidence

- .NET SDK 10.0.201, Avalonia 12.1.6; native arm64/x64 bridges target macOS 15.
- 37 local automated cases cover signatures, account/public isolation, redirects, reply protocol, rule interchange, history, typed navigation/back/forward/restart/offsets, delayed account response rejection, concurrent social state updates, verified author/follow-status fallback, nested comment support, document translation mapping/retry/cancel/cache/stale responses, game metadata cache, inline emoji, font file collisions, cache cleanup, encrypted session restoration/tamper rejection/permissions/independent roots and repeated UI navigation.
- Account storage is now AES-256-GCM local files under `account/`, with mode 0700 for the directory and 0600 for key/ciphertext. Normal application code contains no Keychain calls. The existing session was migrated once outside the app, local restoration verified, and the old Keychain item removed.
- A read-only fresh-process check using the new local session passed: favorites 15, history 19, article 212 blocks and 20 comments. Folder/following endpoints also returned successfully. Reports contain counts and status only.
- Prior public read-only API checks passed feed, search, article, Steam name/AppID search, announcements and official QR challenge. No live account writes were performed.

## Native UI evidence

- Same-content A-reference comparison at 1280×800 in both themes, with shared shell/reader components. See `../design-qa.md` for remaining text/icon differences; exact pixel identity is not claimed.
- English navigation/settings update in place; real native file picker imported Arial into the isolated test root, and removal restored the system font.
- AVKit played Apple's published HLS sample; the displayed time advanced beyond six seconds. Media replacement and removal completed without a crash. A GIF test asset displayed through NSImageView.
- Native sharing picker opened after retaining the picker and adding a visible-window fallback. No share destination was selected.
- Closing/reopening the isolated window retained the application process and recreated the window. UI handlers, image bitmaps and active media are detached on close.
- Apple Translation now detects the source language from the batch and presents the system language download sheet. Download and cancellation paths were exercised. At the last check language availability remained `supported`, not `installed`; a successful on-device translated result remains unverified. The app keeps the original and exposes retry/cancel/error state.

## CI and packaging

The prior macOS runner failed before any step started: “The job was not acquired by Runner of type hosted even after multiple attempts”; GitHub annotated arm64 capacity constraints. Platform-independent tests now use Ubuntu and packaging uses the documented `macos-15-intel` runner, with an Intel runtime startup check. See the PR checks for the current run; a queued run is not a pass.

Both local architectures produce self-contained .app and development DMG artifacts. They use ad hoc signatures. Developer ID signing, notarization, Gatekeeper acceptance, Intel GUI execution and physical macOS 15 execution require separate evidence. Rosetta is unavailable on the development host.

## Remaining real-world acceptance

- Actual account mutations (like/favorite/follow/comment/reply), including service-rejected sends. Protocol/state tests pass; real posting has not been claimed. Failed sends keep account-scoped drafts.
- Completed Apple language installation and a successful translation, animated GIF timing, VoiceOver traversal and long-session memory profiling.
- Exact A text metrics/quote position and generated icon differences; external system UI follows macOS.

## 0.3.1 · 2026-11-18

50 local tests passed. Coverage added for Steam and scoped Heybox search history replay and deletion across restart, and root comment draft restoration and submission through the window using a simulated signed-in account.

Seven public integration checks passed on November 18: Heybox feed, search, article, official QR challenge and Steam name search, AppID search and announcements. The real-data check ran without an account.

On Apple Silicon an isolated running app translated an English Steam sample with installed Apple Translation language resources. The Chinese output and the bilingual view were observed. At 900×640 the app opened an article, switched to comments and back to the body, then returned to the list. Live account writes and Intel GUI behavior remain unverified in this pass. Both architecture packages use ad hoc signing.
