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
| `original_sha256`, `cert_sha256` | pins written by the first build; builds refuse anything else |

## Restore the key on a computer

`openssl enc -d -aes-256-cbc -pbkdf2 -iter 600000 -md sha256 -in release-key.p12.enc -out release-key.p12`
(asks for the passphrase; PKCS12 keystore, alias `svz`, password = the passphrase).
