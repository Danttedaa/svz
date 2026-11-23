# svz-private: private builds of SvZ Enhanced

**Keep this repository private.** It holds the original game (release `original`), your signing key
(encrypted) and the finished APKs (releases `apk-*`). The engine code lives in the public repository
named in `svz-build.json`; nothing from here is ever published.

Progress and problems are posted to the issue **Build status**.

## Two build routes

| workflow | input | when it runs |
|---|---|---|
| `build-from-port.yml` (default) | the prebuilt SvZ port APK pinned in `svz-build.json` (mesflit/svz release); a private copy is kept as release `port-input` | pushes, daily if the engine changed, manual |
| `build-apk.yml` | your original game as release `original` (APK, + OBB, or `.xapk`); full rebuild | when `original` is published, manual |

Both sign with the same key, so their APKs are updates of the same app.

## Phone-only setup

1. **Passphrase.** In your password manager, generate a random password of 24+ characters and save it as
   "SvZ signing key". Then in a browser (desktop site if the menu is hidden): this repository →
   **Settings → Secrets and variables → Actions → New repository secret**, name `SVZ_KEY_PASSPHRASE`, paste it.
2. That's all for the default route: the next run creates your signing key and builds the APK
   (about 15-25 minutes). The issue **Build status** shows progress.
3. **Back up the key** once Build status says it was created: open `signing/release-key.p12.enc` →
   download, and store the file next to the passphrase in your password manager. Both are needed to sign
   updates; neither works alone.
4. **Install:** Releases → newest `SvZ-Enhanced-….apk` → download → open → allow installing from that app.
   It installs next to the original port (different package id); later builds install over it and keep
   its save game. Saves of the original port cannot be moved (its data is not readable by other apps).

## Settings: `svz-build.json`

| key | meaning |
|---|---|
| `engine_repo`, `engine_ref` | public engine repository and branch/tag to build |
| `package` | Android package id: keep it, a different one is a separate app with an empty save |
| `app_name`, `version_code_base`, `keep_builds` | launcher name, versionCode start, how many APK releases to keep |
| `keep_tags` | APK releases the cleanup never deletes (known-good builds, e.g. `apk-r3-45d610042402` = v1003) |
| `original_sha256`, `cert_sha256` | pins written by the first build; builds refuse anything else |

## Rolling back to a known-good build (phone only)

Android does not install an older versionCode over a newer one without uninstalling, and uninstalling
deletes the save game. So do **not** reinstall an old APK file. Rebuild the old engine instead; it gets a
new, higher versionCode, the same key and package, and installs over any newer build with progress kept:

1. **Actions → Build SvZ Enhanced APK (from port) → Run workflow**
2. `engine_ref`: `45d610042402d52527cbfbd1d28a71bfdd64dceb` (v1003, the last build tested on the phone)
3. Install the new release when Build status reports it.

The original v1003 release (`apk-r3-45d610042402`) is kept for reference via `keep_tags`.

## Emulator smoke test

Every build is started on an Android x86_64 emulator: normal start, then the Store and a battle opened
directly. Results (`smoke.txt`, screenshots, filtered logcat) are attached to the release. A failed smoke
test publishes the APK only as a pre-release. The emulator is not your phone: 120 FPS, touch handling and
Samsung power settings are only verified on the device.

## Restore the key on a computer

`openssl enc -d -aes-256-cbc -pbkdf2 -iter 600000 -md sha256 -in release-key.p12.enc -out release-key.p12`
(asks for the passphrase; PKCS12 keystore, alias `svz`, password = the passphrase).
