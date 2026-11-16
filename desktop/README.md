# 盒阅 · HeyRead for macOS

Avalonia 12.1.6 / .NET SDK 10.0.201 / macOS 15+. Independent macOS client on the selected **A · 原生紧凑** design. Android sources and upstream history are preserved. Upstream research baseline: `2f4e8b1e16a0baf4c934448162524de1d2483d47`.

## Build and run

Requires .NET SDK 10.0.201 and Xcode command line tools on macOS. No commercial Avalonia components are used.

```sh
cd desktop
./scripts/build-native.sh
dotnet restore HeyRead.slnx --locked-mode
dotnet run --project src/HeyRead.Desktop
```

For an isolated real-data run, set `HEYREAD_TEST_ROOT` to a dedicated absolute directory. Preferences, cache, fonts, drafts and history use that directory. The Keychain service is also namespaced by a hash of that directory. Test sessions never overwrite the normal account service. Unit tests use an in-memory session store.

`HEYREAD_DEMO=1` explicitly enables the labeled offline sample. It is never substituted for failed network requests.

```sh
./scripts/package-macos.sh osx-arm64
./scripts/package-macos.sh osx-x64
```

Produces self-contained `.app` and DMG files in `desktop/artifacts/`. Both Apple Silicon and Intel native bridges compile against the macOS 15 deployment target. The default signature is ad hoc. To sign and notarize, provide `HEYREAD_SIGN_IDENTITY` and `HEYREAD_NOTARY_PROFILE`; these are optional local environment variables, never committed. Ad hoc artifacts are development builds, not a notarized public release.

## Implementation

- **Core**: signed public requests, account request isolation, feed/search/destination pagination, Steam game search and announcements, QR session protocol, cloud folders/history/following, social request forms, bounded HTML/rich block parsing, Android-compatible block-rule interchange, local library.
- **Desktop**: constructor-injected MVVM composition, shared light/dark theme, Fluent icons, virtualized story list, draggable reader/list split, compact navigation at 900×640, article/comments offsets, text drafts, image gallery, native media and settings.
- **Native**: Keychain, Apple Translation via a SwiftUI-hosted `translationTask` (macOS 15 API), AVPlayerView, animated NSImageView, NSSharingServicePicker, isolated WKWebView verification, and one-use official Tencent CAPTCHA callbacks for public requests.
- **Resources**: simplified/traditional Chinese, English and French base UI resources. Server errors/content retain their original language; extended help text still requires a complete localization pass.

Account cookies go only to HTTPS `api.xiaoheihe.cn`; account redirects are blocked and no account disk cache is configured. Public gateways, Steam, media and verification WebViews have independent transports. Session and CAPTCHA proof `ToString()` redact credentials. Account writes require a UI action and retain drafts on failure. No credentials are stored in preferences or Git.

Steam articles intentionally have no Heybox likes, comments, favorites or author-follow controls. Native system file picking/sharing and official verification are explicit handoff points.

## Validation

Run fast/targeted tests locally; the full desktop suite and dual-architecture packaging run in `.github/workflows/desktop.yml`.

```sh
dotnet test tests/HeyRead.Tests --filter FullyQualifiedName~CoreTests
dotnet test tests/HeyRead.Tests --filter FullyQualifiedName~AppTests
dotnet run --project src/HeyRead.Desktop -- --probe
```

The probe performs read-only public API requests and creates a QR challenge, without logging its key or credentials. It never writes account interactions.

See `docs/VALIDATION.md` for evidence and outstanding manual acceptance. A successful build or protocol fixture does not establish live posting, end-to-end translation, Intel execution, notarization, or pixel-for-pixel design acceptance.
