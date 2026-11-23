# SvZ Enhanced: changes on top of the Godot port

> Current state, exact commits, build commands, test results and next steps: [`HANDOFF.md`](HANDOFF.md).
> Fastest build route (no original game APK): `tools/binpatch/build_from_port.sh`.

Development branch work for a high-refresh-rate SvZ Defense build aimed at the Samsung Galaxy S25 Ultra
(Android 17, arm64-only, 120 Hz). Same rules as upstream: no game code or assets are in this repository;
you build from your own copy of the 3.4.0 APK with `tools/build.sh`.

## Milestone 1: 120 Hz frame pacing (branch `enhanced/m1-frame-pacing`)

### Why the upstream port runs at 60 FPS on a 120 Hz phone

1. `Main._Ready` hard-codes `Engine.MaxFps = 60`.
2. Since Android 15, the system keeps games at 60 Hz unless they request a higher rate with the
   Frame Rate API (`Surface.setFrameRate`) or Swappy. Godot 4.7.2's Android layer reads the refresh rate
   (`GodotIO.getScreenRefreshRate`) but never requests one, and Godot's Swappy integration is Vulkan-only,
   while this port uses the GL Compatibility renderer. Removing the cap alone is therefore not enough.

### What changed

| file | change |
|---|---|
| `godot/src/Engine/Core/FramePacingPolicy.cs` | Pure C# policy: settings parsing, pacing decision, fallback monitor, hold gestures, frame-rate independent `smoothDeltaTime` |
| `godot/src/Engine/Core/FramePacing.cs` | Godot side: settings file, `Surface.setFrameRate` request via the `AndroidRuntime` bridge, VSync on, limiter control, fallback, gestures, FPS readout |
| `godot/src/Engine/Core/Main.cs` | 60 FPS cap removed; FramePacing hooked into init, frame loop, resume and level load; `smoothDeltaTime` made frame-rate independent |
| `godot/src/Engine/Impl/AppImpl.cs` | `Application.targetFrameRate` no longer overrides the player's choice; `Screen.currentResolution` reports the real refresh rate |
| `tools/dev/frame_audit.py` | Static audit for frame-rate-dependent game logic (run it on `godot/src/Game` after `prepare_sources.sh`) |
| `tests/` | Off-device tests (policy unit tests + headless Godot 4.7.2 runtime test), CI in `.github/workflows/tests.yml` |

Pacing rules:

- When the display runs at the target rate, the software limiter is off (`Engine.MaxFps = 0`) and VSync
  paces frames. A sleep-based limiter at exactly the refresh rate drifts against VSync and causes judder.
- When it does not (target 60 on a 120 Hz panel, or the system refused 120 Hz), the limiter caps at the
  target and the display is asked for the target rate, so 60 FPS mode also lets the panel drop to 60 Hz.
- The display rate is re-read 1.5 s after each request and every 5 s, and the decision is updated.
- Game physics already ran on a fixed 0.02 s step with an accumulator (`Main._Process`), independent of
  the frame rate. Animation and legacy particles use frame delta time. The game's own scripts are
  `deltaTime`-driven (see the audit below).

### Settings

File: `user://svz_enhanced.cfg` (created with defaults on first start), section `[display]`:

| key | default | meaning |
|---|---|---|
| `target_fps` | `120` | 30, 60, 90 or 120 |
| `auto_fallback` | `true` | drop to `fallback_fps` for the rest of the session if the target cannot be held |
| `fallback_fps` | `60` | |
| `show_fps` | `false` | FPS / frame time / display Hz readout, top left |
| `request_display_rate` | `true` | call `Surface.setFrameRate` on Android |
| `allow_non_seamless_switch` | `false` | allow refresh-rate switches that may blank the screen briefly |
| `log_stats` | `true` | one `[svz-fps] stats fps=... p95=... missed=...` logcat line every 5 s |

In game, with no menu changes:

- hold **4 fingers** for 1.5 s: switch between 120 and 60 FPS (saved)
- hold **5 fingers** for 1.5 s: show/hide the FPS readout (saved)

Fallback rule: after a 10 s grace period (restarted after every level load), frames are measured in 5 s
windows. A window is slow when more than 20 % of frames missed a VSync, or the average is below 80 % of the
target. Two slow windows in a row switch to `fallback_fps` until the app restarts. Frames longer than
0.25 s (loading, app switching) are ignored. Device temperature is not read.

Command-line options for PC runs: `--fps=60`, `--show-fps`, `--no-fallback`.

Logs: `adb logcat -s godot | grep svz-fps`.

### Verification status

| check | result | where |
|---|---|---|
| Diff applies cleanly to upstream `f00a75c` | pass | `git am` on the fork's `main`; identical diff, `git diff --check` clean |
| Policy unit tests (38) | pass | `tests/run_tests.sh`, .NET 8, Linux x86_64 |
| `FramePacing.cs` compiled against GodotSharp 4.7.2 and run in the Godot 4.7.2 runtime (17 checks: settings file, gestures, fallback, `Engine.MaxFps` really limits the frame rate) | pass | headless Godot 4.7.2 .NET, Linux x86_64 |
| Settings survive an app restart (three separate processes, 11 checks) | pass | same; found and fixed a bug: `auto_fallback` was saved as off after a period at 60 FPS |
| Refresh-rate bridge call chain against stand-in Java objects (9 checks: both paths, API 30 and 31+ overloads, strategy flag, missing hops) | pass | same |
| Bridge methods exist, public and un-minified, in the official Godot 4.7.2 Android template; `GodotApp` extends `GodotActivity`; JavaClassWrapper resolves by runtime class and superclasses | pass | DEX inspection of `android_release.apk`, Godot 4.7.2 sources |
| `Surface` constants and overloads (`CHANGE_FRAME_RATE_ONLY_IF_SEAMLESS` = 0, `ALWAYS` = 1; 3-arg API 31, 2-arg API 30) | checked | Android API reference |
| Build/verify tools and workflow guards (31 Python tests) | pass | `tests/tools` |
| Full port compile with game sources, APK export | **not verified** | needs the original APK: runs in the private pipeline |
| `Surface.setFrameRate` on a real device, 120 FPS on the S25 Ultra | **not verified** | `tools/verify/device_check.py` on the phone |

No emulator was used: the cloud session had no KVM and no access to the Android SDK download servers.

### Known limitations

- Samsung's Game Optimizing Service / Game Booster and battery saver can still cap the refresh rate; the
  FPS readout shows what the panel actually runs at.
- Device temperature is not monitored; the fallback reacts to missed frames only.
- The fallback is one-way per session on purpose (no oscillation between 60 and 120).
- No in-menu setting yet: the gestures are the only in-game control.

### Testing on the phone (after a build)

1. `nix develop -c tools/build.sh --install`
2. `adb logcat -s godot | grep svz-fps`: expect `Surface.setFrameRate(120) sent` and, about 1.5 s later,
   `display after request: 120Hz -> maxFps=0 ... vsyncPaced=True`.
3. Hold 5 fingers for 1.5 s to show the FPS readout; play a level; hold 4 fingers to compare 60 FPS.
4. Developer options > Show refresh rate confirms the panel rate independently.

## Milestone 2: private build pipeline (installable APKs without re-supplying the game)

See [`ci/private/README.md`](ci/private/README.md). The template in `ci/private/repo/` goes into a
private repository; setup needs only a phone browser (one secret, one release upload). The first run creates
the release key (stored only encrypted), later runs build, sign, verify and publish privately.
This public repository publishes nothing: `build.yml` lost its release steps and refuses to run in a public
repository; `tests/tools/test_workflows.py` keeps it that way.

| file | purpose |
|---|---|
| `ci/private/repo/` | private repository template: workflow, `ci/signing.sh`, `ci/config.py`, `svz-build.json` |
| `tools/set_android_preset.py` | package id, app name, versionCode, versionName for one build (`tools/build.sh --package ...`) |
| `tools/verify/verify_apk.py` | arm64-v8a, no INTERNET permission, package, versionCode, signer, bridge methods, apksigner |
| `tools/verify/cert_fingerprint.py` | signing certificate check (`SVZ_EXPECT_CERT_SHA256` in `tools/build.sh`) |
| `tools/verify/device_check.py` | on a PC with adb: SurfaceFlinger present timestamps + `[svz-fps]` stats -> PASS/FAIL at 120 FPS |
| `tools/probe/` + `.github/workflows/probe-apk.yml` | 120 Hz probe app: the frame-pacing code on a phone, no game files |

## Milestone 3: faster normal victory transitions (in verification)

- Patch the compiler-generated `InGameImpl.PostWinDelay` coroutine's single
  `WaitForSeconds` duration from the legacy configured ~7 game-seconds to **1.0 game-second**.
- The original `RegisterWin()` (rewards/profile save) and `onFinishGameRequest()`
  calls are preserved, in order, by a fail-closed Mono.Cecil patcher that checks
  the shape of the iterator before modifying it. The original compiled game
  and its private assets are not committed to this public repository.
- This is **not yet a real-device-verified feature**. A final-wave ending
  could be shortened; validate endings and standard wins before merging.
  Tap-to-skip and shorter screen loading are separate later changes.
- The fake-port test includes a synthetic iterator to catch changes to the
  expected IL contract. The first private APK must verify the real iterator
  has the same pattern.

## Milestone 4: real-time victory timing correction (draft, testing)

Phone test of v1002 still reported approximately seven real seconds after the
previous `WaitForSeconds(1.0f)` patch. This was a **scaled** Unity coroutine
wait, while the win screen enables 0.1× timeScale and gradually restores it.
The delay was not reliably one real second.

The new patch changes **only** that coroutine's single yield expression to
`Svz.VictoryTiming.WaitRealSeconds(originalGameSeconds)` returning a nested
`IEnumerator` measured using monotonic real time. The existing `CoroutineRunner`
executes nested iterators, so no engine or game-data rewrite is required.

- Default 1.0 real second, independent of slow motion.
- Reward registration and final scene transition remain in original order.
- Verified by a pure .NET test that waits in real time for both 7s and 9s
  legacy duration inputs, and by stand-in Cecil patch tests.
- Still must test the **actual APK** on the S25 Ultra for normal wins,
  rewards, save persistence and special ending behavior.

## Milestone 5: phone-visible fallback diagnostics (draft)

When the performance monitor decides that 120 FPS cannot be held, capture the
**last measured 5-second window and display refresh rate before changing to
60 FPS**. The optional five-finger FPS overlay then shows both the current
60 FPS mode and the trigger, e.g. `Fallback from 120: 76.0 fps, 34% missed (slow), panel 120Hz`.
This distinguishes heavy rendering from a Samsung 60 Hz / power-saving restriction
without requiring a laptop or ADB logs.

The original safe fallback thresholds and behavior are unchanged; diagnostics
do not disable the fallback or force 120 FPS during overloaded combat.

## Milestone 6 — stuck corpse failsafe (experimental)

Reported bug: some defeated zombies remain visible and motionless. The game
already awards enemy kill credit and resources **before** moving an enemy to
`CharactersManager.mDyingCharacters`. The usual `isOver` condition waits for
animation / `ModelMelter` callback and sometimes appears to remain false.

In that existing dying-list cleanup pass alone, the bytecode patch retains the
original `isOver` check and adds a conservative 12-second **unscaled wall time**
failsafe for a still-dead character. Revived characters reset the timer and
follow their original return-to-battle path. No general active enemies, damage,
health, rewards, save or animation callbacks are changed. This must be tested
in game before calling the frozen-zombie defect fixed. A log line beginning
`[svz-death]` is emitted if the failsafe triggers.

## Milestone 7 — HD bitmap-font selection (experimental, not visual QA)

The legacy `FontSetFinder` originally selects the **nearest** font
resolution, so a modern landscape display can select the lower-resolution
768px bitmap-font atlas and stretch thin strokes. This experimental patch
selects the game's existing **1536px** asset set when the Android viewport
height is in [700,1536], if that atlas is listed by the game. It preserves
the original nearest-resolution choice for small screens and missing atlases.

The patch changes no font assets, colours, hitboxes or legacy GUI layout.
**Visual verification is required** on Shop, Allies, tutorial and victory
screens: sharper bitmap sources do not guarantee improved contrast or
identical font size. If text clips or degrades, revert this experiment.

## Milestone 8 — allocation reduction for animation and particles (draft)

The shipped restoration's `AnimationSystem.Step` and
`AnimationSystem.FinishStates` each call `List<AnimationState>.ToArray()`,
and `Particles.Update` does the same for emitters. These hot synchronous
foreach loops allocate fresh arrays even when the lists are unchanged, at up
to 120 frames per second.

The patcher now replaces **only these three known call sites** with generic
`ArraySnapshots.Snapshot<T>(List<T>)`, which maintains a weak-key,
exact-length reusable buffer per list. The array order and contents are
refreshed each call, including after list count changes. The patcher rejects
an unexpected IL sequence, and stand-in plus pure C# tests check behavior.
No enemy logic, rewards, textures, or visuals are changed.

This is a targeted **allocation** optimization, not yet a measured FPS gain:
compare on-device before and after during a similarly effect-heavy wave.

## Audit of M3–M8 (2026-11-23, against the real port APK)

Method: the pinned port APK (`d9dfad74…`) was run **headless on Linux** (official Godot 4.7.2 .NET + .NET 9
runtime, the APK's own `assets/` as the project), with a local-only harness assembly injected into a copy of
`SvZPort.dll` (skips tutorials/narration, keeps the hero alive, kills enemies with every attack type, reports
corpses still in `CharactersManager.mDyingCharacters`, per-subsystem CPU/allocation probes). Screenshots
were rendered with Xvfb + Mesa llvmpipe at 2340×1080. Neither the harness nor screenshots (game art) are in
this repository.

| milestone | finding | action |
|---|---|---|
| M3/M4 victory wait | correct for normal wins (exit 1.6 s after victory, `RegisterWin` then `onFinishGameRequest`, each once) but also cut the one-time **wave-70 story ending** (`EndGameMessage`) to 1 s | M9: that ending keeps its configured time (measured 7.5 s) |
| M5 fallback diagnostics | correct; no gameplay effect | none |
| M6 stuck-corpse timeout | a workaround, not a fix; counted wall time, so a pause or the slow-motion finish could cut a death animation | M9: root cause fixed; the net now counts game time |
| M7 HD fonts | **regression** (v1006/v1008): the port draws glyphs at the font's native pixel size, so the 1536 set drew all text 2× larger; Store dialog overflowed, captions cut | M10: HD atlas drawn at the original size |
| M8 snapshot reuse | correct (no re-entrancy into the same list inside those loops); small effect | kept |

## Milestone 9: frozen zombies, root cause

Reproduced headless: **every enemy killed by an explosion, by the sonic attack, or as the last enemy of a
wave froze** (35/35 Explosion/Sonic kills stayed until M6's 12 s removal). Cause: the game's shared tagged
`dieexplode` animation clip has events (`OnAnimStart` at 0 s, `OnAnimDone` at 1/30 s) but no curves. The
port computed a clip's length from its curves only, so the clip had length 0: its state never reached
`normalizedTime` 1 and its events never fired, so `CharacterModelController.readyToVanish` stayed false. Only
two clips in the whole game have no curves (this one and an unused emitter clip).

- Engine source: an event-only clip lasts until its last event. Binary patch: `AnimationClip.get_length`
  passes its value through `AnimationFixes.ClipLength` (only zero lengths change).
- Result: 0 frozen corpses (Explosion/Sonic stress, waves 8/25/70; wave 70: 303/303 vanished through the
  game's own path); the explosion's `SpawnEffectPrefab` event now plays again.
- M6 stays as a safety net for unknown cases: 12 **game**-seconds (`Time.time` from the call site; the
  longest legitimate death is ~7.1 s), logged as `[svz-death]` via `GD.Print`.
- Wave-70 story ending: see the audit table.

## Milestone 10: readable Store and Allies text

- The 1536 font atlas is still loaded on 700–1536 px screens, but each loaded font's glyph metrics are
  scaled once (hook after `SUILabel`'s font load) by `nearest/1536 × text scale`. At the default 100 % the
  layout is identical to the original game, with sharper glyphs; the game's own word wrapping measures the
  scaled glyphs.
- Contrast: every `GUIText` is drawn twice; the first pass draws a soft 8-direction halo from a white,
  alpha-only (LA8) copy of its atlas: warm light behind dark text, dark behind light text. Dark captions on
  the red Store scroll and the grey "UNLOCK AT WAVE n" labels on the Allies tab become legible.
- **Hold 3 fingers for 2 s** to switch the text size between 100 % and 115 % (130 % clipped wrapped Store
  captions, so it is not offered). Settings: `user://svz_ui.cfg`, `[text] scale` (80–150) and `contrast`.

## Milestone 11: crowded-wave hitches

Profiled on the real port, wave 70, crowd kept alive (probes around every subsystem of `Main._Process`,
CPU sampling with `dotnet-trace`):

| subsystem | CPU / frame (desktop) | allocated / frame |
|---|---|---|
| `Render.Sync` (skinning ~50 % of the thread, then particle mesh rebuilds) | 2.8 ms | 85 KB |
| `AnimationSystem.Update` (`Apply`) | 0.8 ms | 281 KB |

Garbage collections every ~5 s took 150–400 ms. Allocation volume was not the main factor: GodotSharp's
`DisposablesTracker` held 100,000–450,000 undisposed interop objects between collections, and every
collection had to finalize them. A large share came from `MeshCache`, which rebuilds every particle mesh
every frame: one `Variant` per vertex attribute (each owning a native copy of the array), the arrays
`Godot.Collections.Array`, and the empty blend-shape array and LOD dictionary that the
`AddSurfaceFromArrays` binding allocates for its null defaults.

Shipped (binary patch and engine source):

- `MeshCache.Get/GetSubsets`: interop temporaries are disposed as soon as Godot has its copy; the empty
  defaults are shared (`MeshArrays`).
- `Render.SyncRenderer` read an environment variable per particle renderer per frame, and
  `Main.DebugAutomation` fetched the command line twice per frame: both are read once (`Reuse`).

Result (interleaved A/B, 3 rounds, wave 70, process CPU time per frame at the normal 120 FPS cap):
**CPU −15 % (1.75 → 1.50 ms), worst garbage-collection hitch −55 % (243–314 ms → 110–137 ms)**, no
frozen corpses. Desktop CoreCLR numbers; the phone runs Mono, so check the FPS readout in a crowded wave.

Tried and rejected (measured, not shipped):

- Caching bone poses / one `SetBonePose` call instead of three setters: bones are posed in world space, 98 %
  change every frame, and the cost is Godot's native side. No gain.
- Reusing `AnimationSystem.Apply`'s list and dictionaries (shared or one set per animated object): cut
  allocations by 56 % and GC events by half, yet consistently cost +1.5 ms CPU per frame. The mechanism
  was not identified, so it was not shipped.
- Benchmarks must be interleaved A/B runs: this VM's speed drifted by 2× between runs.


## Milestone 12: remove obsolete rating popup; lighter text outlines (draft)

On-device screenshots of v1010 showed the obsolete Glu credit-for-rating
prompt over the normal victory screen, noisy text halos, and a single 50 FPS
reading on a Store modal. To improve this without modifying game art or saves:

- **Rating prompt:** `DebriefingImpl.DisplayStats` still invokes the old Android
  `ApplicationUtilities.IsBuildType("amazon"/"google")` branches on specific
  victories. The patcher replaces only those two boolean calls with literal
  false in that method. All other debrief logic (achievements, stat awards,
  unlocks, cash, saves) is left intact; no permanent profile flag is changed.
- **Readability:** the earlier eight-copy 1.5-pixel halo around each glyph is
  reduced to a four-direction, one-pixel outline at 1080p, with slightly
  less opacity. This preserves source fonts, glyph dimensions, layout and
  the original character and UI artwork. It cuts extra halo draw calls
  per glyph from eight to four, although this is not yet a measured FPS gain.
- Synthetic patched-assembly tests check that the rating prompt is disabled
  without skipping the reward bookkeeping. Device screenshot QA is needed.
- Shop scrolling clips partially visible lower rows as they scroll; do not
  alter the StoreListCell layout or card bounds without validating the full
  scroll interaction in gameplay.
