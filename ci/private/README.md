# Private APK pipeline

Ready-to-install SvZ Enhanced APKs, built automatically, without the game ever entering a public place.
Setup needs only a phone browser.

```
public  Danttedaa/svz           engine code, tools, tests          no game files, no APKs, no releases
            |  checkout of an engine commit (git ls-remote / actions/checkout)
private Danttedaa/svz-private   template: ci/private/repo/ (workflow, ci/*.sh|py, svz-build.json, README)
            |  release "original": the game, once        secret: SVZ_KEY_PASSPHRASE
            |  signing/release-key.p12.enc: release key, encrypted with the passphrase
            v
        releases apk-r<run>-<commit>: signed arm64 APK + verify.json      issue "Build status": progress
```

## Who does what

| step | who | where |
|---|---|---|
| create the private repository, give the Claude GitHub app access to it | you | GitHub in a browser |
| copy `ci/private/repo/` into it, keep it updated | Claude | git push |
| add the secret `SVZ_KEY_PASSPHRASE` (24+ random characters from your password manager) | you | repo Settings → Secrets and variables → Actions |
| create the signing key, store it encrypted, pin its certificate | the workflow | first run with the secret |
| upload the original game as release `original` (APK, + OBB if any, or an APKPure `.xapk`) | you | repo Releases in a browser |
| verify the original, pin its SHA-256, build, sign, verify, publish privately | the workflow | every run |
| follow runs, read logs, fix engine problems | Claude | API (runs, logs, issues) |
| back up `signing/release-key.p12.enc` next to the passphrase | you | download in the browser |
| install the APK, check 120 FPS | you | phone |

## Security model

- The workflow refuses to run in a public repository; nothing from it is uploaded as an Actions artifact.
- The release key exists in plain form only on the runner during a build (deleted at the end). In the
  repository it is AES-256 encrypted (PBKDF2-SHA256, 600,000 iterations) with the passphrase, which is a
  GitHub secret: it cannot be read back, not by Claude either. Backup = encrypted file + passphrase.
- The key is never replaced automatically: if the file disappears while a certificate is pinned, builds
  stop and ask for the backup. A different key would make every update an uninstall (save lost).
- The original is checked before use: package `com.glu.samuzombie`, version 3.4.x, Unity data present,
  APK signature valid (signer recorded in the status issue), SHA-256 pinned on first use. OBB files carry
  no signature; the built app has no INTERNET permission either way.
- `tests/tools/test_workflows.py` and `tests/tools/test_private_repo.py` (CI in the public repository) keep
  these properties from regressing.

## Install, saves, versions

- Package id `io.github.danttedaa.svz` (`svz-build.json`): separate from the original game and the
  upstream port, so installs never conflict. Keep it: another id is another app with an empty save.
- versionCode = `version_code_base` + run number: every build installs over the previous one, keeping saves.
- APKs are release assets of the private repository (private, not counted as Actions artifact storage).
- GitHub Free includes 2,000 Actions minutes a month for private repositories; a build is expected to take
  30-60 minutes (not measured yet).

Full private-repository instructions: `ci/private/repo/README.md`.
