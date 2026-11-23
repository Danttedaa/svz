# Private APK pipeline

Ready-to-install SvZ Enhanced APKs, built automatically, without the game ever entering a public place.

```
public  Danttedaa/svz           engine code, tools, tests          no game files, no APKs, no releases
            |  (git ls-remote / checkout of a commit; optional "engine-updated" ping)
private Danttedaa/svz-private   release "original": your APK, once   secrets: signing key
            |  .github/workflows/build-apk.yml (this folder)
            v
        private releases apk-r<run>-<commit>: signed arm64 APK + manifest.json + verify.json
```

- **One-time input:** your original Samurai vs Zombies Defense 3.4.0 APK (plus OBB only if the data is not
  inside the APK), stored as the private release `original` of the private repository. Its SHA-256 is
  pinned in the variable `SVZ_ORIGINAL_SHA256`; every build checks it.
- **Builds:** manual, daily when the engine has a new commit, or immediately after a push to the public
  repo if the optional notifier is configured (`.github/workflows/notify-private-build.yml`).
- **Output:** a release of the private repository (only you can download it). Older builds beyond
  `SVZ_KEEP_BUILDS` (default 5) are deleted automatically; the `original` release is never touched.
- **Guards:** the workflow stops if the repository is public; the public repo's own build workflow has the
  same guard and no release steps; `tests/tools/test_workflows.py` fails CI if that ever changes.

## Getting the original APK legitimately

Use your own copy: for example pull it from a phone or tablet where the game is still installed
(USB debugging on; Windows PowerShell with Android platform-tools):

```powershell
cd C:\SvZ
adb shell pm path com.glu.samuzombie          # prints package:/data/app/.../base.apk
adb pull /data/app/.../base.apk SvZ-3.4.0.apk  # use the path printed above
adb shell ls /sdcard/Android/obb/com.glu.samuzombie/   # an OBB here? pull it too
```

Do not download it from APK mirror sites: besides the rights question, those files are often modified.

## One-time setup (about 10 minutes)

Run in **WSL (Ubuntu)** on Windows, or any Linux/macOS terminal. In Windows PowerShell, `wsl --install -d Ubuntu`
first if WSL is not installed yet.

```bash
# WSL Ubuntu terminal
sudo apt update && sudo apt install -y gh openjdk-17-jre-headless unzip python3 git
gh auth login                                   # GitHub.com, HTTPS, browser login
gh repo create Danttedaa/svz-private --private  # must be private
cd ~ && git clone https://github.com/Danttedaa/svz && cd ~/svz && git checkout enhanced/m1-frame-pacing
cp /mnt/c/SvZ/SvZ-3.4.0.apk ~/svz-original.apk  # the file from the step above
tools/private_build_setup.sh --repo Danttedaa/svz-private --apk ~/svz-original.apk \
    --engine-ref enhanced/m1-frame-pacing
```

The script refuses a public repository, creates your release signing key in `~/.svz-signing/`, stores it as
secrets of the private repository (through stdin, never on a command line), uploads the original once,
sets the variables, installs the workflow and starts the first build (30-60 minutes; watch it with
`gh run watch --repo Danttedaa/svz-private`).

**Back up `~/.svz-signing/` offline** (e.g. an encrypted USB stick or your password manager). Android
installs an update only if it is signed with the same key; without the key the only way to install a new
build is to uninstall the old one, and that deletes the save game.

## Installing on the Galaxy S25 Ultra

- Phone: open the private repository's **Releases** in the GitHub app or a browser where you are logged in,
  download the newest `SvZ-Enhanced-*.apk`, open it, allow installing from that app when asked.
- PC (Windows PowerShell, platform-tools):
  ```powershell
  cd C:\SvZ
  gh release download --repo Danttedaa/svz-private --pattern "SvZ-Enhanced-*.apk" --clobber
  adb install -r (Get-ChildItem SvZ-Enhanced-*.apk | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
  ```
  `-r` replaces the app and keeps its data (the save game).

Check 120 FPS afterwards (Windows PowerShell, from your clone of the public repo):

```powershell
cd C:\SvZ\svz
py -3 tools\verify\device_check.py --package io.github.danttedaa.svz
```

## Saves, package id and versions

- Package id `io.github.danttedaa.svz` (variable `SVZ_PACKAGE`): a separate app from the original game and
  from the upstream port (`com.mesflit.svz`), so installing it never conflicts with them. Its save data is
  its own: progress from those apps does not move over automatically. Changing the package id later
  creates yet another app with an empty save, so keep it.
- The save format is the game's own and is not changed by the enhancements.
- versionCode = `SVZ_VERSION_CODE_BASE` (1000) + the workflow run number, so every build installs over the
  previous one.

## Reproducibility

Pinned inputs: engine commit, original APK SHA-256, Nix flake lock (Godot 4.7.2 .NET, .NET 9, Android SDK),
GitHub Actions pinned to commit SHAs. Each release carries `manifest.json` with all of them and the APK's
SHA-256. Rebuilding the same inputs gives the same game; the APK bytes may still differ (zip timestamps
and signing), which is why the manifest records inputs, not just the output hash.

## Variables and secrets (set by the setup script)

| name | kind | purpose |
|---|---|---|
| `SVZ_KEYSTORE_B64`, `SVZ_KEYSTORE_PASSWORD`, `SVZ_KEYSTORE_ALIAS` | secret | release signing key |
| `SVZ_CERT_SHA256` | variable | the build refuses to sign with any other key |
| `SVZ_ORIGINAL_SHA256` | variable | the build refuses any other original APK |
| `SVZ_ENGINE_REPO`, `SVZ_ENGINE_REF` | variable | what to build (default `Danttedaa/svz`, `main`) |
| `SVZ_PACKAGE`, `SVZ_APP_NAME`, `SVZ_KEEP_BUILDS`, `SVZ_VERSION_CODE_BASE` | variable | optional |

## Cost

GitHub Free includes 2,000 Actions minutes a month for private repositories. One build is expected to take
roughly 30-60 minutes (not measured yet); the daily check costs under a minute when nothing changed. APKs are
stored as release assets, not as Actions artifacts, so they do not use the small artifact storage allowance.
