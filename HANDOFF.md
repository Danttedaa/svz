# SvZ Enhanced: handoff (2026-11-23, 16:00 EET)

For whoever continues this work (human or another AI assistant). Everything needed is in this repository;
nothing important exists only in a workspace. Game files and signing keys are **never** committed here.

## Goal and priority

Installable, privately distributed **SvZ1 Enhanced** APK for a Samsung Galaxy S25 Ultra (Android 17,
arm64-only, 120 Hz), 120 FPS where supported, 60 FPS fallback. SvZ2 later. Current priority: a reproducible
private APK build. No new major features until the first build runs on the phone.

## Repositories, branches, PRs (exact state)

| what | where | commit | state |
|---|---|---|---|
| upstream port | `mesflit/svz` `main` | `f00a75c5cb79` | MIT, no game files; its release `latest` = `svz.apk` |
| fork | `Danttedaa/svz` (public) `main` | `f00a75c5cb79` | unchanged upstream |
| PR #2 (security only) | `security/no-public-game-releases` → `main` | `8cd05559ac8c` | **open, not draft, waiting for owner approval**; 1 file: `build.yml` loses its public release steps, refuses public repos |
| PR #1 (all work) | `enhanced/m1-frame-pacing` → `main` | `0ffb8857bd74` | **draft, do not merge**; 13 commits (below) |
| probe app | fork release `probe-latest` (pre-release) | built from `69e84d58a3be` | public; no game files |
| private build repo | `Danttedaa/svz-private` | – | **does not exist yet** (needs the owner) |

Rules from the owner: keep PR #1 draft and PR #2 separate; merge nothing without approval; do not touch the
owner's Jarvis repository; keep game assets, APKs containing them and keys out of public repos and chat.

PR #1 commits (oldest first): `7629902` 120 Hz frame pacing · `52ee19c` settings persistence + fix ·
`16c38db` refresh-rate bridge hardening · `8c71eb3` CI publishing guards · `0b9d762` private pipeline v1 ·
`e640c8c` probe app · `caddcf0` / `ddb4261` probe build fixes + phone-first private template ·
`f8655f4` venv fix · `02c4b33` probe pre-release · `fac4fd8` private checkout fix ·
`69e84d5` **build from prebuilt port APK (tools/binpatch)** · `0ffb885` private README.

## What exists

| path | what |
|---|---|
| `godot/src/Engine/Core/FramePacing.cs`, `FramePacingPolicy.cs` | 120 Hz frame pacing: `Surface.setFrameRate` via AndroidRuntime bridge, VSync pacing, one-way 120→60 fallback, `user://svz_enhanced.cfg`, 4-finger hold = 120/60, 5-finger hold = FPS readout, `[svz-fps] stats` logcat line every 5 s |
| `godot/src/Engine/Core/Main.cs`, `Impl/AppImpl.cs` | source-build integration (used by the full rebuild route; **never compiled with real game sources**) |
| `tools/binpatch/` | **fast route**: `SvzEnhanced/` (FramePacing as `SvzEnhanced.dll`), `SvzPatch/` (Mono.Cecil call-site patch of `SvZPort.dll`), `repack.py`, `build_from_port.sh`, `emulator_smoke.sh`, `get_cecil.sh` |
| `ci/private/repo/` | template for the private repo: `build-from-port.yml` (default), `build-apk.yml` (original APK route), `ci/ensure_key.sh`, `ci/signing.sh`, `ci/config.py`, `svz-build.json`, `README.md` |
| `tools/verify/` | `verify_apk.py` (ABI, package, versionCode, INTERNET, signer, bridge methods, apksigner), `device_check.py` (adb + SurfaceFlinger + game stats → PASS/FAIL at 120 FPS), `cert_fingerprint.py` |
| `tools/probe/` | 120 Hz probe app (FramePacing without the game) |
| `tools/build.sh` | upstream full build + `--package/--app-name/--version-code/--version-name`, signing-key guards |
| `tests/` | `run_tests.sh` (all suites), policy tests, headless Godot runtime tests, bridge stand-ins, restart test, binpatch stand-in (`tests/binpatch/FakePort`), Python tool + workflow-guard tests |
| `.github/workflows/` | `tests.yml`, `probe-apk.yml` (only public release: probe), `notify-private-build.yml` (inert until configured), `build.yml` (upstream; release steps removed on this branch) |
| `ENHANCED.md`, `ci/private/README.md` | design notes, settings, security model |

## Build: enhanced APK from the prebuilt port APK (fast route, recommended)

Why: the mesflit release APK (`svz.apk`, 420,654,616 bytes, SHA-256
`d9dfad74e7692206bb69bc77f9225cff75e5133928217f0003aaa3bb0529cbea`, signer "CN=SvZ Port" certificate SHA-256
`CCF14BCA217B417A286DCAACBFC999B71626B78E6DD2B41D05523F2B0299E532`) contains the converted game data and
`SvZPort.dll` (plain .NET IL). The enhancement ships as `SvzEnhanced.dll`; Godot's Android host loads
assemblies by name from `assets/.godot/mono/publish/<arch>/` (`modules/mono/mono_gd/gd_mono.cpp`,
`load_assembly_from_pck`). `SvZPort.dll` gets 6 call-site patches, each matched against the exact original IL.
Godot's sparse pack directory `assets/assets.sparsepck` (PCK v4, sizes + MD5 per file) is updated.

### In the private repository (what the owner will use)

One-time (owner, phone browser): create private repo `svz-private` with a README; give the Claude GitHub app
access if it is limited to selected repos; add secret `SVZ_KEY_PASSPHRASE` (24+ random characters, kept in
the owner's password manager). Then (assistant, git): copy `ci/private/repo/` into the repo root and push to
`main`. The push starts `build-from-port.yml`:

1. private-repo guard → `ci/ensure_key.sh` (first run: creates the key, stores only
   `signing/release-key.p12.enc` = PKCS12 + AES-256/PBKDF2-600k, pins `cert_sha256` in `svz-build.json`, commits)
2. port APK: private release `port-input` if present, else download `port_apk_url`; SHA-256 + signer pinned;
   uploads `port-input` on first use (so later mesflit updates of `latest` do not break builds)
3. `engine/tools/binpatch/build_from_port.sh` from `engine_ref` (default `enhanced/m1-frame-pacing`)
4. emulator smoke test (`ReactiveCircus/android-emulator-runner`, API 34 x86_64, `continue-on-error`)
5. release `apk-r<run>-<engine12>` with the APK, `verify.json`, `smoke.txt`, `screenshot.png`; issue "Build status"

Owner afterwards: back up `signing/release-key.p12.enc` next to the passphrase; install from Releases.

### Locally (Linux or WSL; needs network for NuGet and git)

Requires: .NET SDK 8+, `git`, `python3` with `androguard==4.1.4` (use a venv: `python -I` ignores user
site-packages), Android build-tools `zipalign` + `apksigner`, JDK `keytool`.

```bash
# Linux / WSL terminal
git clone https://github.com/Danttedaa/svz && cd svz && git checkout 0ffb8857bd74
curl -fsSL -o /tmp/port.apk https://github.com/mesflit/svz/releases/download/latest/svz.apk
echo "d9dfad74e7692206bb69bc77f9225cff75e5133928217f0003aaa3bb0529cbea  /tmp/port.apk" | sha256sum -c
python3 -m venv /tmp/venv && /tmp/venv/bin/pip install androguard==4.1.4
export SVZ_PY=/tmp/venv/bin/python KS_PASS='use-a-long-random-passphrase'
keytool -genkeypair -keystore /tmp/test.p12 -storetype PKCS12 -storepass:env KS_PASS -keypass:env KS_PASS \
  -alias svz -dname "CN=test" -keyalg RSA -keysize 2048 -validity 30      # throwaway key for testing only
tools/binpatch/build_from_port.sh /tmp/port.apk /tmp/out/SvZ-Enhanced.apk \
  --ks /tmp/test.p12 --ks-alias svz --ks-pass-env KS_PASS --version-code 1001
```

Offline NuGet (as in the cloud session): set `SVZ_NUGET_CONFIG` to a `nuget.config` whose only source is
`<Godot_v4.7.2-stable_mono_linux_x86_64>/GodotSharp/Tools/nupkgs`.

Reference results from the cloud session (input SHA-256 `d9dfad74…`, commit `69e84d5` code):

| file | SHA-256 |
|---|---|
| original `publish/arm64/SvZPort.dll` | `9c47ba58ac7632de080032cbe95fc3ff00b678bafac67ca87078ef88d0b899c5` |
| patched `publish/arm64/SvZPort.dll` (deterministic; identical across runs) | `f09052cc13f9b1f048c47aaaa4147dab48c5a32d548def99f0ace84643d99c04` |
| patched `publish/x86_64/SvZPort.dll` | `b45ab49876c563ebcbc26166e1de94212912f7a9eb000e61dab8498b159a22dd` |

The signed APK's hash depends on the signing key. With a test key the APK passed: apksigner, arm64-v8a +
x86_64, package `io.github.danttedaa.svz`, label "SvZ Enhanced", versionCode, no INTERNET, bridge methods,
all 9,782 pack directory entries matching, no `mesflit` string left in the manifest.

## Build: full rebuild from the original game APK (second route)

`tools/build.sh` (upstream, Nix) or private workflow `build-apk.yml` (input: release `original` with the
original 3.4.0 APK/OBB/XAPK; checks package `com.glu.samuzombie` 3.4.x, Unity data, signature; pins SHA-256).
No legitimately owned original is available to the owner; this route is untested end to end.

## Tests and results (cloud session, Linux x86_64; no emulator, no device)

```bash
tests/run_tests.sh /path/to/Godot_v4.7.2-stable_mono_linux_x86_64   # .NET 8 SDK + official Godot 4.7.2 .NET build
```

| suite | result |
|---|---|
| Python tool/workflow tests (`tests/tools`) | 52 passed |
| FramePacing policy tests | 39 passed |
| Godot 4.7.2 headless runtime test | 17 passed (incl. `Engine.MaxFps` really limits: 31.4 FPS at cap 30) |
| refresh-rate bridge with stand-in Java objects | 9 passed |
| settings survive restart (3 processes) | 11 passed |
| binpatch on stand-in `SvZPort.dll` | patched, check + resolution OK, double patch refused |
| GitHub Actions on `0ffb885` / `69e84d5` | Tests ✅, Probe APK ✅ (built, verified, published `probe-latest`), Notify ✅ |
| real port APK, local | patch + resolution + repack + sign + verify ✅ (not run) |

Static checks done against Godot 4.7.2 sources and the official Android template: bridge methods
(`GodotActivity.getGodot`, `Godot.getRenderView`, `GodotRenderView.getView`, `AndroidRuntimePlugin.getActivity`,
`createRunnableFromGodotCallable`) public and un-minified; `GodotApp` extends `GodotActivity`; keystore env
vars `GODOT_ANDROID_KEYSTORE_RELEASE_*` override the preset; Android `Surface` constants
(`ONLY_IF_SEAMLESS` = 0, `ALWAYS` = 1; 3-arg API 31, 2-arg API 30).

## Known problems / not verified

1. **Nothing has run on a device or emulator yet** (probe included). 120 FPS is not verified.
2. The private repository does not exist; neither private workflow has ever run. Expected first-run risks:
   KVM/emulator start-up on the GitHub runner, swiftshader GLES3 for Godot, the `git push` of pins.
3. `SvzEnhanced.dll` targets net8.0; the app runs .NET 9 Mono on Android. Backward compatible in principle,
   not yet observed at runtime. If it fails to load: retarget `tools/binpatch/SvzEnhanced` to net9.0.
4. The repacked APK keeps the old `SvZPort.pdb` files (unused on Android; harmless).
5. The source-build route (`Main.cs`, `AppImpl.cs` with decompiled game code) has never been compiled.
6. mesflit replaces `latest` on every push: if `svz.apk` changes before the first private run, the pinned
   SHA-256 fails on purpose; review the new APK, then update `port_apk_sha256`/`port_signer_sha256`, and check
   that `svzpatch` still matches the IL (it refuses otherwise).
7. Saves of the installed mesflit app cannot be migrated (`allowBackup=false`, not debuggable, different
   signer). SvZ Enhanced is a separate app; its own save survives later builds (same key).
8. Legal: the port APK and archive APKs redistribute Glu's game without a licence; no licensed source exists.
   Inputs and outputs stay private; the owner accepted this by asking for the port route.
9. Debug arguments `--fps`, `--no-fallback` can be persisted by a later in-game toggle (PC-only, minor).
10. Upstream issues remain: OnGUI screens missing, Pachinko untested (see `NOTES.md`).

## Next actions (in order)

1. Owner: create `svz-private` + access + `SVZ_KEY_PASSPHRASE` (see above). Owner: approve or reject PR #2.
2. Assistant: `add_repo Danttedaa/svz-private` (push), copy `ci/private/repo/*` (incl. `.github/`) to its root,
   push to `main`; follow the run (`gh api repos/Danttedaa/svz-private/actions/runs`, logs:
   `.../actions/runs/<id>/logs`, status issue "Build status"); fix failures in `Danttedaa/svz` on the PR branch.
3. Owner: install `SvZ-Enhanced-*.apk` from the private Releases; back up the encrypted key file.
4. Verify on the phone: Developer options → show refresh rate; hold 5 fingers 1.5 s for the FPS readout;
   or `py -3 tools\verify\device_check.py --package io.github.danttedaa.svz` (Windows PowerShell, adb). Only then
   call 120 FPS verified. Optional earlier check: the `probe-latest` APK.
5. Then (both via `tools/binpatch`, no asset recreation):
   - **UI Readability & Scaling**: text is dynamic (Unity bitmap fonts via `SUILabel`, set chosen by screen
     height: 320/480/640/768/1536; on 2340×1080 the 768 set is stretched ~1.4×). All text is drawn in
     `Svz.GuiSystem` `DrawText` (`godot/src/Engine/Render/Gui.cs`): higher font set, outline/shadow, optional
     backing panel, high-contrast option, 100/125/150 % scale without clipping. Needs before/after screenshots.
   - **Fast Loading & Transitions**: `InGameImpl.PostWinDelay` waits `postWinTime = 7.0` game seconds
     (`endGameTime = 9.0` after wave 70; values from the reconstruction's `Registry/Config.txt`, to confirm
     in 3.4.0) before `RegisterWin` (rewards, save) and loading the results screen; slow-mo finisher makes it
     ≈8.75 s real time (model). Cap the real-time wait + tap to skip, run `RegisterWin` exactly once; measure.
6. SvZ2: Decomp-And-Recomp reconstruction needs IL2CPP/ARM64 (Unity 2017.4 Mono has no ARM64); R-1.1 APK is
   armeabi-v7a + x86 only (verified).
