# 小黑盒Community For Mac

Avalonia 12.1.6 / .NET SDK 10.0.201 / macOS 15+. Independent macOS client with a [compact reading layout](docs/design/compact-reader.png). Android sources and upstream history are preserved. Upstream research baseline: `2f4e8b1e16a0baf4c934448162524de1d2483d47`.

## Build and run

Requires .NET SDK 10.0.201 and Xcode command line tools on macOS. No commercial Avalonia components are used.

```sh
cd desktop
./scripts/build-native.sh
dotnet restore HeyRead.slnx --locked-mode
dotnet run --project src/HeyRead.Desktop
```

For an isolated real-data run, set `HEYREAD_TEST_ROOT` to a dedicated absolute directory. Preferences, cache, fonts, drafts and history use that directory. Login sessions use a separate `account/` directory within that root, so test sessions cannot overwrite normal sessions. Unit tests use an in-memory store. The normal app does not call Keychain.

`HEYREAD_DEMO=1` explicitly enables the labeled offline sample. It is never substituted for failed network requests.

```sh
./scripts/package-macos.sh osx-arm64
./scripts/package-macos.sh osx-x64
```

Produces `xiaoheihe-community-formac.app`, update ZIP and DMG files in `desktop/artifacts/`. Both Apple Silicon and Intel native bridges compile against the macOS 15 deployment target. Local development and desktop CI use ad hoc signatures by default. Set `XIAOHEIHE_SIGN_IDENTITY` to a Developer ID Application identity in your keychain to sign the app and DMG. Set `XIAOHEIHE_NOTARY_PROFILE` to an existing `notarytool` keychain profile to submit and staple both. `XIAOHEIHE_SIGN_KEYCHAIN` optionally selects the keychain that holds the signing identity and notary profile.

## Implementation

- **Core**: signed public requests, account request isolation, feed/search/destination pagination, Steam game search and announcements, QR session protocol, cloud folders/history/following, social request forms, bounded HTML/rich block parsing, Android-compatible block-rule interchange, local library.
- **Desktop**. The floating navigation dock uses LiquidGlassAvaloniaUI for live blur and refraction over the feed. Search and feed controls are available from the dock. Selecting a feed tab refreshes its content. The virtualized list supports pull-to-refresh at the top and pagination at the bottom. The window starts with a compact feed. Selecting an article expands the window to the right while retaining the feed width and scroll position. Close the reader from its title tab or with Escape. Articles flow into comments in the same scroll view, with inline replies, account-scoped drafts, an image gallery and native media playback.
- **Native**: Apple Translation via a SwiftUI-hosted `translationTask` (macOS 15 API), AVPlayerView, animated NSImageView, NSSharingServicePicker, isolated WKWebView verification, and one-use official Tencent CAPTCHA callbacks for public requests.
- **Resources**: simplified/traditional Chinese, English and French base UI resources. Server errors/content retain their original language; navigation, settings, confirmations and translation states share matching resource keys. Server content retains its original language.

Account cookies go only to HTTPS `api.xiaoheihe.cn`; account redirects are blocked and no account disk cache is configured. Public gateways, Steam, media and verification WebViews have independent transports. Session and CAPTCHA proof `ToString()` redact credentials. Account writes require a UI action and retain drafts on failure. Sessions are AES-256-GCM encrypted in `account/session.bin`; the random 32-byte local key is in `account/session.key`. The directory is mode 0700 and both files are mode 0600. This protects against accidental plaintext disclosure and access by other filesystem users; an already authorized process running as the same user can read both files. Sign-out deletes both files. Credentials are never stored in preferences or Git.

Steam articles intentionally have no Heybox likes, comments, favorites or author-follow controls. Native system file picking/sharing and official verification are explicit handoff points.

## Validation

Run fast/targeted tests locally; the full desktop suite and dual-architecture packaging run in `.github/workflows/desktop.yml`.

```sh
dotnet test tests/HeyRead.Tests --filter FullyQualifiedName~CoreTests
dotnet test tests/HeyRead.Tests --filter FullyQualifiedName~AppTests
dotnet run --project src/HeyRead.Desktop -- --probe
dotnet run --project src/HeyRead.Desktop -- --probe-account
```

The public probe performs read-only public API requests and creates a QR challenge, without logging its key or credentials. The account probe loads the local session and reports only counts and status, without credentials, identifiers or private content. Neither probe writes account interactions.

See `docs/VALIDATION.md` for evidence and outstanding manual acceptance. A successful build or protocol fixture does not establish live posting, end-to-end translation, Intel execution, notarization, or pixel-for-pixel design acceptance.


## GitHub Release and automatic updates

The macOS product is `xiaoheihe-community-formac`, displayed as **小黑盒Community For Mac**. Bundle ID: `com.3egirlsdream.xiaoheihe-community-formac`. The fork repository and Android project remain intact. Existing local account/history/font data stays under the legacy HeyRead data directory to preserve sign-in.

`desktop/Version.props` is the sole release version source. Increasing its numeric X.Y.Z version and committing to `main` starts `.github/workflows/macos-release.yml`. It runs tests, creates arm64/x64 self-contained apps, signs every Mach-O inside out, verifies ZIPs, and publishes the complete `mac-vX.Y.Z` Release only after all seven assets are uploaded. Recommitting the same published version is a no-op; downgrades are rejected. Android tags remain independent.

The release workflow runs on `main` and requires the following Actions secrets.

- `MACOS_DEVELOPER_ID_P12_BASE64` and `MACOS_DEVELOPER_ID_P12_PASSWORD` hold a password-protected PKCS#12 export of the Developer ID Application identity for team `AXBYDPHN88`.
- `MACOS_NOTARY_KEY`, `MACOS_NOTARY_KEY_ID` and `MACOS_NOTARY_ISSUER_ID` hold the App Store Connect team API key and its identifiers for Apple notarization.
- `MACOS_UPDATE_SIGNING_KEY` signs the update manifest with ECDSA P-256. Keep the private key backed up securely. Replacing it requires a public-key migration.

The release job uses a temporary runner keychain and checks the Developer ID team and stapled notarization tickets on the ZIP's app and the DMG before publishing. After configuring the secrets on GitHub, use **Run workflow** on `main` to publish an already committed, unpublished version.

Clients check GitHub's stable releases at startup and through About/menu → Check for updates. Only a greater numeric version prompts for installation. Clicking automatic update downloads the process architecture's ZIP, verifies the pinned manifest signature and SHA-256, rejects unsafe archive paths, then verifies Bundle ID, version, architecture and code signature. A same-directory staging bundle is prepared before exit. The helper preserves a backup, replaces the app after the parent exits, launches the new version and waits for a validated startup receipt before deleting the backup. A failed launch restores the old app. Account/session files are never in the replacement bundle. Read-only disk-image locations must be moved to a writable installation location first.

For a read-only local check: `xiaoheihe-community-formac.app/Contents/MacOS/xiaoheihe-community-formac --check-updates`. For isolated update acceptance, build a lower version with `XIAOHEIHE_BUILD_VERSION`, launch with `HEYREAD_TEST_ROOT` and `HEYREAD_DEMO=1`, and perform the actual UI update against the published GitHub Release.
