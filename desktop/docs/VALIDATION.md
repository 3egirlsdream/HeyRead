# macOS validation · 2026-11-16

## Verified locally

- SDK 10.0.201, Avalonia 12.1.6, macOS arm64 build; Swift bridge arm64 targets macOS 15.
- 18 targeted automated cases passed before final packaging: upstream signer vector, cookie isolation, redirect blocking, reply form fields, Steam-only interaction guard, rule interchange/limits/duplicates, cloud history content wrappers, CAPTCHA proof validation, local drafts/offsets and repeated window/settings navigation, background image decoding.
- Read-only real public API probe: Heybox feed 10 rows; search 6; article 22 blocks; Steam name search 10; AppID 570 resolved; announcements 10; official QR challenge returned. No account writes performed by the probe.
- Real native UI: discovery and article navigation, compact settings and dark theme, QR scan/confirmation completed by user, cloud favorites returned 15 rows, followed stream returned 30 rows, and 900×640 list-to-reader switching succeeded.
- The first live history check showed zero normalized rows; inspection found missing `content` unwrapping. This was fixed and covered by a regression case. A later authenticated real-history check is still required.
- First native image run exposed a background-thread read of Avalonia `Height`; decoding now captures width on the UI thread and has an asynchronous regression case.

## Pending manual acceptance

- Full authenticated history and session restoration with the final scoped Keychain implementation; cloud folder variations and following filters across account changes.
- Actual like/favorite/follow/comment/reply writes, including service-rejected posting and account verification. These mutate a real account and are not implied by read-only validation.
- Apple Translation language download, successful on-device result, cancel/failure; native video/GIF/share and imported fonts with real media/font files.
- All extended localized help text, VoiceOver traversal, long-session memory profiling, and exact A-design visual acceptance.
- Native execution on an Intel Mac and macOS 15 hardware. Cross-compilation alone is not execution evidence.
- Developer ID signing, notarization and Gatekeeper installation. The supplied development artifacts use ad hoc signing unless signing variables are provided.

The local report in the task's `outputs/heyread-macos/` directory records the final builds and smoke checks. This document does not claim full feature or visual sign-off.
