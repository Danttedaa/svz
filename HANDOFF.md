# Audio settings continuation: 2026-11-26

User requested a settings section, then explicitly deferred graphics settings
until graphics enhancement is done. This pass adds AUDIO ONLY: master volume,
music volume, sound effects volume and mute. No graphics/FPS controls were added.

The existing Options screen gains an Audio settings button; About, Credits,
Privacy and the original Back button remain. The pause menu gains Settings.
Closing the modal returns to that same screen/pause menu. The native overlay
scales with the viewport, pauses the scene tree and preserves any previous pause
state. Android Back and Escape close it. Restore QuitOnGoBack only after the current
Window Back event completes, since its notification precedes its auto-quit signal. Current and pending game input is cleared
on open/close so touches cannot leak into movement or menu actions.

Audio is routed through an owned master bus with separate Music/Effects children,
leaving legacy AudioListener bus gain/mute and per-source fades intact. Exported
music clips use the BGM_ prefix; looping enemy ambience remains Effects. Settings
are stored in user://svz_audio.cfg, independently of profile/progress saves.
Cutscene audio also routes through the owned master bus.
Zero volume mutes its bus exactly; corrupt and out-of-range preferences recover.

Validation: full suite passes: 72 primary Godot runtime checks, eleven real-touch/keyboard
UI interaction checks, two checks in a separate process for persistence, the
existing bridge/restart checks, and 19 patched-behavior checks including held and
pending input reset. The exact pinned port passes the new IL patch and helper
resolution. A real Options render confirms the new entry fits without covering
existing buttons. Native panel preview is legible; target-phone verification
remains necessary. The settings entry lifecycle is patched into OptionsImpl and
PauseMenu for the production binary route. Full-source integrations must also
wire the PauseMenu entry's show/hide calls to the generated game class.

---

# Bone pose performance continuation: 2026-11-25

Phone build 1047: 102 FPS with 2x AA in the reported busy battle; deliberate
cheap-troop stress tests fell back after about 74 FPS with AA off. Stress scenes
are explicitly beyond the user's usual troop load. CPU totals in those captures
were 8.7-10.8 ms, with render sync 4.3-5.8 ms. These are different scenes and
cannot establish an exact improvement ratio or minimum instantaneous FPS.

Next pass replaces separate per-bone position/scale/rotation submissions with
one `Skeleton3D.SetBonePose` call, caching exact duplicate poses by skeleton and
bone index. Bone data/animation frequency and character designs are retained.
The original transform remains passed by reference to avoid an extra managed
copy. The IL patch checks the decomposition shape and refuses unexpected branch
entries or exception boundaries before replacing that block.

Validation: full suite passed, including 55 Godot runtime checks and 18 patched
behavior checks. Pose equivalence covers rotations, nonuniform scales, reflected
and sheared bases, unchanged poses and subpixel movement. Local headless benchmark
of 20,000 changing bone updates (five runs, warmed, median) measured previous
path 15.45 ms versus single-call path 3.39 ms. This is a bone-update microbenchmark,
not a whole-game or phone speedup. The exact pinned real port passes patch checks
and helper resolution, and renders an animated game world through MainMenu.
The next private APK and target-phone performance acceptance remain required.

---

# Graphics and performance continuation: 2026-11-25

Accepted phone baseline: engine `3ed660b7aef8962c87f629d6e795cca1f8ea514d`
(private build 1046). User confirmed the font rendering is perfect. The Shop
screenshot reports 120 FPS. The subsequent combat screenshot reports fallback
from roughly 70 FPS to 60; sustained 120 FPS combat is NOT accepted yet.
Preserve the original characters, accepted font rendering and single existing
hold gesture. Do not treat Store FPS or a tutorial scene as crowded combat proof.

Current pass adds adaptive 2x MSAA only to camera SubViewports (not the GUI),
and skips unchanged bridge-owned visibility, layers, shadow mode, transforms,
viewport sizes and bone scales. Cache keys are weak node identities; exact value
comparisons preserve subpixel movement and changing poses. These helpers must
not be used on state another system writes. Mesh/skeleton replacement gets a
fresh cache via its new Godot object. Bone positions/rotations remain live.

Remove optional MSAA and retry the selected target once before dropping to
60 FPS. New-level loads reset that quality budget. Existing frame pacing,
120 ceiling and persistent settings are retained. `--graphics=original` gives
an A/B rendering baseline without another touch gesture. The existing diagnostic
FPS overlay now includes CPU process/render-sync time and AA state. CPU times
exclude asynchronous GPU rendering and are not GPU measurements.

Local validation uses the exact pinned port APK and real Godot 4.7.2 runtime.
Entering through MainMenu and pressing Start creates a valid game world; loading
InGame directly previously missed initialization and produced a blue tutorial.
A fresh-profile tutorial is useful for visual comparisons but does not validate
crowded combat or sustained phone FPS. Never publish game images/assets/APKs
in the public repository. Phone verification of this graphics pass remains open.

---

# Latest acceptance work: 2026-11-25

Draft PR: https://github.com/Danttedaa/svz/pull/25

Engine code `6d9980eca17bc8ba1b066059dafc07d7dcdd5322` passed the hosted
Tests workflow (run 45647488346): Python/tool checks, 42 pacing policy assertions,
10090 corpse cases, 70 font assertions, 9 snapshot checks, Godot runtime/bridge/
restart tests, and 18 patched-behavior checks. Android acceptance is separate.

Build 42 failed during Android activity recreation. Its log shows OnPause,
OnStop, engine destruction, reuse of an initialized fragment and force quit;
this is not evidence of a managed game-code exception. The next smoke version
sets a stable landscape orientation and display size before first launch, and
stops the activity before subsequent size changes. Fresh-install diagnostic
stages run independently while retaining all prior failures.

The coding executor became unavailable during this continuation. GitHub changes
and hosted checks remain usable. The private repository now has a
`Review emulator diagnostics` workflow that exposes image previews only inside
private job logs after each build. It downloads only the private smoke artifact;
it neither executes artifact contents nor handles APKs or signing keys.

Completion must not be claimed yet: valid native/HD text images, a nonzero
crowded-battle/death workload, target-phone acceptance and the legacy OnGUI/
Pachinko scope still need resolution. See RESTORATION_TOOLKIT.md for reusable
components and verification boundaries.

---

# Renderer blocker identified: 2026-11-25

Build 41 / run 45630730319 / artifact 15031318728 proves the blank images
are actual blank Godot frames (sampledColors=1), not Android overlays. Both
SceneShaderGLES3 and CanvasShaderGLES3 fail linking with:
`Fragment shader active uniforms exceed GL_MAX_FRAGMENT_UNIFORM_VECTORS (261)`.
The active renderer is the old emulator SwiftShader 4.0.0.1 GLES translator.
Android window focus is the game, no obscuring window, power Awake.

M28 changes the private emulator GPU from deprecated `swiftshader_indirect`
to `swangle` (SwiftShader through ANGLE). No phone-renderer or font changes.
Shader-link failures now explicitly fail smoke even if the app stays alive.
Next: inspect the new private diagnostics artifact; require real Store images
before evaluating native 768 versus original-HD 1536. Build 41 remains unverified.

---

# Current continuation: 2026-11-25

Branch: `enhanced/m27-native-font-baseline`. Preserve original designs, 120 FPS
ceiling, accepted combat fixes, and exactly one two-finger text toggle.

Private build 39 (`3c66e46292f6`, run 45594557562) remained alive and exercised
Store original, original-HD, native-768 baseline, and battle. Font logs verified
768/1536 selection and actual prefiltered glyph draws. Smoke correctly failed:
Android screencaps were only 1598/5338 bytes; no valid visual comparison yet.
Battle reported zero kills, so that run does not prove crowded-wave/death behavior.

Next diagnostic build adds `--test-capture-frame=<path>` only to emulator APK
variants. It captures the Godot viewport after drawing at 20 seconds, keeps the
app running, and logs dimensions plus sampled color count. The normal APK has no
capture argument. `viewport-*.png` complements, and does not override, Android
screen verification. Blank-screen failures now log focus/surface/power summaries.

Private workflow must publish `emulator-diagnostics` with smoke PNGs and text only,
7-day retention. APKs, inputs and keys must never be workflow artifacts. Retrieve
this artifact through the GitHub connector to inspect frames without browser auth.
Compare viewport and Android screenshots before changing font rendering again.

Local verification: Python 61 passed / 1 skipped; policy 42 passed; corpse safety
10090 cases; font policy 70 assertions; snapshots 9; binary-patch behavior 18 checks.
Godot runtime, bridge and restart tests passed; the new capture callback compiles.
Android capture itself still needs the private emulator run.

---

# Historical handoff (superseded by the continuation above)

# SvZ Enhanced: handoff (2026-11-23, evening)

For whoever continues this work (human or another AI assistant). Game files, decompiled game code, game
screenshots and signing keys are **never** committed to this public repository.

## Current state

| item | state |
|---|---|
| last build tested on the phone (S25 Ultra) | **v1003** (`apk-r3-45d610042402`, engine `45d6100`): 120 FPS with power saving off, ~1 s victory, rewards OK |
| v1004–v1008 | built + emulator smoke only; **v1006/v1008 contain the M7 font regression** (text 2x, Store dialog overflows) |
| this branch (`claude/svz-enhanced-resume-czy9ax`) | M1–M8 + M9 (frozen-zombie root cause), M10 (readable text), M11 (GC hitches), pipeline updates |
| private repo `Danttedaa/svz-private` | builds from `engine_ref` in `svz-build.json`; `keep_tags` protects v1003; manual runs accept an `engine_ref` (rollback) |

What changed and why, with measurements: `ENHANCED.md` (sections "Audit of M3–M8", M9, M10, M11).

## How the port was tested without a phone (repeatable, not committed)

The pinned port APK runs on Linux: unzip its `assets/` into a folder (it is a Godot project:
`project.binary`, `.godot/`, `data/`), put the x86_64 `SvZPort.dll` (patched or not) into
`.godot/mono/temp/bin/Debug/`, and start the official Godot 4.7.2 .NET editor binary with
`--headless --path <folder> -- --level=2` (2 = InGame, 3 = Store, 1 = MainMenu). It needs a .NET 9 runtime
(`DOTNET_ROOT` to a folder with `host/fxr` + `shared/Microsoft.NETCore.App/9.x` from the NuGet package
`microsoft.netcore.app.runtime.linux-x64`, and `DOTNET_ROLL_FORWARD=LatestMajor`). Screenshots: the same with
`xvfb-run`, `--rendering-driver opengl3 --resolution 2340x1080 -- --level=3 --shots=8` (Mesa llvmpipe).

A local harness assembly (injected with Mono.Cecil at the end of `Main._Ready`, never shipped) skipped
tutorials, kept the hero alive, killed enemies with chosen attack types, reported corpses stuck in
`CharactersManager.mDyingCharacters`, and measured CPU/allocations per subsystem. Benchmarks on the cloud VM
drift up to 2x between runs: compare builds only with interleaved A/B runs, using process CPU time per frame.

Known, pre-existing (also in the unmodified port): occasional caught `NullReferenceException` in the
animation events `SpawnEffectPrefab` / `HideBodyPart` during forced kills; harmless, not investigated.

## Next steps

1. Phone: install the newest private build (after its emulator smoke passed), check Store/Allies text,
   explosions (no frozen zombies; `[svz-death]` lines in logcat would mean an unknown remaining case),
   victory timing incl. rewards, and the FPS readout in a crowded wave (hold 5 fingers).
2. If anything is wrong: rebuild v1003 via Actions → Run workflow → `engine_ref` = `45d610042402d52527cbfbd1d28a71bfdd64dceb`
   (installs over newer builds and keeps the save; never reinstall an old APK file).
3. Remaining crowded-wave cost is Godot-native skinning (`Skeleton3D` bone poses) and particle mesh rebuilds;
   GodotSharp still tracks ~120k interop objects from other sources between collections (find them next).

---

# Earlier handoff (2026-11-23, 16:00 EET), kept for history


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
