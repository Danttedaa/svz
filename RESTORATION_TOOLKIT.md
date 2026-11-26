# SvZ Restoration Toolkit

Reusable code and verification procedures for SvZ1 and a later SvZ2 restoration.
This document identifies existing components; it does not claim that SvZ2 is ported
or that its game code and assets are compatible.

## Compatibility before reuse

SvZ1's current fast route patches a pinned .NET port assembly with Mono.Cecil.
The patcher matches exact IL and refuses unexpected inputs or a second patch.
SvZ2 needs its own input audit, engine/ABI route and call-site adapters. Do not
apply SvZ1 IL patterns or game-specific reflection fields to SvZ2 without inspection.

Keep game code, assets, screenshots and APKs out of the public source repository.
Keep signing keys private and encrypted at rest. Reuse algorithms and tooling,
not another game's unreviewed binaries or data.

## Components

| Component | Source | Portable part | Integration work |
|---|---|---|---|
| Frame pacing | `godot/src/Engine/Core/FramePacingPolicy.cs` | Target sanitization, fallback windows, frame statistics, hold-gesture policy | Choose SvZ2 runtime hooks and fixed-timestep policy |
| Android refresh rate | `godot/src/Engine/Core/FramePacing.cs` | Surface frame-rate request, resume handling, settings | Verify actual SDK level, render view and refresh-rate support |
| Victory delay | `tools/binpatch/SvzEnhanced/VictoryTiming.cs` | Unscaled wait and exactly-once completion pattern | Identify reward/save callback and each ending sequence |
| Corpse cleanup | `tools/binpatch/SvzEnhanced/DeathCleanup.cs` | Bounded cleanup with live/invalid-object safeguards | Audit character fields, attack types and death lifecycle |
| Snapshot reuse | `tools/binpatch/SvzEnhanced/ArraySnapshots.cs` | Ownership-aware reusable list snapshots | Confirm consumers do not retain or mutate buffers |
| Particle buffers | `tools/binpatch/SvzEnhanced/MeshArrays.cs`, `Reuse.cs` | Reuse arrays and release interop temporaries | Match engine mesh API, growth/shrink behavior and renderer ownership |
| Font metrics | `tools/binpatch/SvzEnhanced/UiFontQuality.cs` | Scale metrics and wrapping together; select atlas independently | Audit SvZ2 font sets and label geometry |
| Original glyph sampling | `tools/binpatch/SvzEnhanced/UiText.cs` | Optional filtering of original glyphs, bounded contrast | Visually compare native atlas and HD paths; preserve original designs |
| Settings skin | `godot/src/Engine/Core/SettingsSkin.cs` | Original-art lookup gated by exact texture size, procedural fallback, large touch targets | Find SvZ2's dialog/button texture ids and sizes |
| Binary patching | `tools/binpatch/SvzPatch/` | Exact preconditions, post-patch checks, double-patch refusal | New signatures and IL matches for every SvZ2 hook |
| APK repack/verification | `repack.py`, `tools/verify/` | Package, ABI, signature, manifest and pack integrity checks | Pin SvZ2 inputs and package independently |
| Android smoke | `emulator_smoke.sh`, `smoke_variant.py` | Isolated diagnostic variants, logs and image capture | Map actual menu/battle entry points and playable save state |
| Private builds | `ci/private/` | Same-key updates, increasing versionCode, rollback and private diagnostics | Separate configuration, secrets and acceptance evidence |

## Verification levels

Keep these results separate in reports:

1. Policy tests prove algorithm behavior on controlled inputs.
2. Stand-in binary-patch tests prove the expected hook structure and callback behavior.
3. Real input patch/repack checks prove compatibility with one pinned input.
4. Android emulator checks prove that the tested paths actually run and render.
5. Phone acceptance proves the behavior on the target device and real display.
6. Crowded-battle measurements prove performance under an identified workload.

None of the earlier levels substitutes for a later one. Killing zero enemies is
not corpse verification. A level-load log is not a usable screenshot. Emulator
FPS is not evidence of phone FPS.

## Emulator lessons

The legacy `swiftshader_indirect` backend produced blank frames with Godot 4.7.2:
both CanvasShaderGLES3 and SceneShaderGLES3 reported uniforms exceeding the
fragment-vector limit. Direct viewport captures had one sampled color. Shader
failures must fail the smoke result even if the process stays alive.

The private emulator now uses SwiftShader through ANGLE (`swangle`). Its result
still needs runtime confirmation. Fix orientation and display size before launching
Godot; stop the activity before changing its display dimensions. Android activity
recreation can destroy the running engine. Never hide this by retrying the same
stage and calling it passed.

Capture both Android's screen and Godot's post-draw viewport. Compare them with
focus, surface, power and process-exit diagnostics. Keep diagnostics in private
artifacts for at most seven days. Never include APKs, game inputs or keys in those
artifacts.

## User-facing constraints

Preserve recognizable character designs and the original visual identity.
The current target is a 120 FPS ceiling with measured fallback, readable text
without clipping or layout growth, and no more than one phone hold gesture.
Debug controls belong in diagnostic variants or tools, not hidden gesture menus.

## Final SvZ1 acceptance

Before calling the Enhanced work complete, verify Store/Allies/dialog/HUD text
with actual images; test a crowded battle, deaths and rewards; verify pause/resume
and an update retaining the save; measure the cap on the target phone. Separately
resolve or explicitly scope the legacy OnGUI screens and Pachinko verification.
The historical build notes do not establish these results for the latest commit.

## Original font coverage

`FontTextureCoverage` restores Unity GUI/Text semantics: atlas alpha supplies
coverage, while the label material supplies color. Exported Alpha8 atlases can
have black RGB with clean alpha. Multiplying that RGB makes both white foreground
and black shadow black, producing doubled, tangled lettering. This is a rendering
error, not evidence that the original glyph silhouette needs replacement.

Normalize font-only textures to white luminance and unchanged alpha once per
atlas. Both source and assembly-patched draw paths share the helper. Sprites,
glyph geometry, text layout and gesture policy are unchanged. Runtime tests cover
all 256 alpha values, source immutability and foreground/shadow tint behavior.
The Store was rendered locally with the pinned port and Godot 4.7.2; Android
verification and device acceptance remain separate requirements.

## Render bridge cost and quality

`RenderQuality` caches bridge-owned state per weak Godot object identity and
skips exact duplicate writes. Animated bone scale is a common unchanged value;
positions and rotations are not frozen. Do not use this cache for shared state
that another system can modify. Recreated mesh nodes/skeletons get fresh state.

Camera-only 2x MSAA smooths geometry while the separate GUI retains its accepted
font draw path. Under sustained overload, remove optional MSAA and retry the
selected FPS once before the existing fallback. `--graphics=original` disables
MSAA for comparisons. The diagnostics record managed process/render-sync CPU
time plus native writes skipped; neither timer measures asynchronous GPU cost.

For real game-world comparisons, launch MainMenu (`--level=1`) and tap Start
(`--tap=3:0.5:0.74`) rather than jumping directly into InGame. Require an actual
nonzero combat workload before claiming battle performance. Local software
rasterization and Store FPS cannot establish phone GPU performance.

## Single-call bone poses

The original skinning bridge sends position, scale and rotation separately and
decomposes each transform in managed code. `RenderQuality.BonePose` submits the
whole transform with `Skeleton3D.SetBonePose`, allowing one native decomposition.
Exact duplicate transforms are skipped; moving bones keep the full animation
update rate. Cache ownership remains the skeleton instance and bone index.

Verify pose equivalence against the old path for rotation, nonuniform scale,
reflection and shear, plus tiny movements and duplicate frames. The assembly
adapter verifies the original contiguous decomposition and branch boundaries.
Benchmark changing poses against the previous scale-cached path; do not equate
a bone-update microbenchmark speedup with a whole-game FPS gain.

## Audio settings and modal input

`GameSettings` owns a three-bus audio hierarchy beneath the existing Master bus,
so user gains multiply source fades without changing legacy listener state.
Classify known BGM_ clips as music, not all looping sources: combat ambience can
loop too. Route reused one-shot players back to Effects. Save audio preferences
separately from progress and recover malformed values conservatively.

The current production adapter adds an Audio settings entry to OptionsImpl and a
Settings entry to PauseMenu; do not remove the original Options links. Modal
open/close clears both current and pending Unity input, preserves prior tree pause
and Back handling, and leaves the native controls active while gameplay pauses.
For a full-source game build, wire PauseMenu construction/destruction to
ShowPauseEntry(true/false); source and patched audio playback use the same bus
helpers. No graphics settings are part of this pass, per the owner's request.

## Opaque world sampling and material synchronization

The port's converted world textures can lose original mipmaps. Restore missing
mips on a duplicate Image and use anisotropic samplers only for opaque spatial
main/secondary textures. Exclude alpha, cutout and particle/additive shaders,
lightmaps and GUI atlases. Bound dimensions and cumulative allocation; do not
claim texture-detail reconstruction from filtering alone. Keep an isolated old
sampling comparison flag without changing AA or introducing a new gesture.

Cache bridge-owned surface materials per node and mesh revision. Invalidate on
ClearSurfaces even when the mesh and material identities stay the same: particle
meshes rebuild in place. Changed materials and replacement meshes must update.
Benchmark native interop separately from whole-game FPS, and validate an actual
crowded workload on the target phone before claiming combat improvement.

## Native-styled settings with original art

Reference the installed game's own dialog textures at runtime instead of copying them.
Gate each by exact dimensions so different data falls back to a procedural theme with
the same palette. Keep Godot controls (real touch, focus and Back handling); only the
theme changes. Verify both skins: the runtime tests have no game data, the pinned port does.

## Degenerate bone poses

Single-call `Skeleton3D.SetBonePose` is only safe for well-conditioned bases. Hidden,
dismembered or flattened bones produce collapsed/coplanar transforms; native Godot then
logs an error with a backtrace and substitutes identity rotation. Check axis-length ratio
and volume, and use the original managed decomposition for the rest. Count native errors
in tests with a `Logger` (`OS.AddLogger`); count them in real battles too.

## Crowded-battle profiling

Use a workload with a measured character count, not a tutorial. Interleave A/B runs and
compare per-window worst frames and missed frames as well as medians: error or GC bursts
hide in averages. `perf` resolves managed frames with `DOTNET_PerfMapEnabled=1` and
`DOTNET_EnableWriteXorExecute=0` (profiling only). Desktop GodotSharp is MinOpt; interop
costs are overstated relative to Android release builds.

## CPU-aware anti-aliasing

Only remove GPU work under overload when the measured CPU time leaves GPU headroom to
gain. When CPU-bound, keep MSAA and use the existing fallback. Record the decision in logs.
