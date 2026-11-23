# SvZ Enhanced: changes on top of the Godot port

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
| Policy unit tests (33) | pass | `tests/run_tests.sh`, .NET 8, Linux x86_64 |
| `FramePacing.cs` compiles against GodotSharp 4.7.2 and runs in the Godot 4.7.2 runtime: settings file, gestures, fallback, `Engine.MaxFps` really limits the frame rate (17 checks) | pass | headless Godot 4.7.2 .NET, Linux x86_64 |
| Full port (`Main.cs`, `AppImpl.cs` with game sources) compiles | **not verified** | needs the decompiled game sources from your APK |
| `Surface.setFrameRate` call reaches the render surface through the AndroidRuntime bridge | **not verified** | needs an Android device; code follows Godot 4.7.2's Java API (`GodotActivity.getGodot`, `Godot.getRenderView`, `GodotRenderView.getView`) with a view-tree search as a second path |
| 120 FPS on the S25 Ultra | **not verified** | needs a device build and test |
| Gameplay timing identical at 60 and 120 FPS | **not verified on device**; static audit of the reconstructed SvZ1 scripts found no frame-count-dependent gameplay logic | `tools/dev/frame_audit.py` |

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
