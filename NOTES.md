# SvZ Defense 3.4.0 -> Unity 6 arm64 port

Source: the original 3.4.0 APK (game data in assets/bin/Data, from the APK or the OBB)
Engine: Unity 3.4.2f3, Mono, armeabi-v7a only, package com.glu.samuzombie

## Layout
- work/apk-unzipped : unzipped APK (no res/, no hashed data)
- work/apk/assets/bin/Data : Unity data + Managed/*.dll (tools/build.sh extracts it)
- work/decomp/java : jadx output of classes.dex
- work/decomp/cs : ilspycmd projects (Assembly-CSharp ~40k lines, firstpass, UnityScript)
- work/decomp/apktool : decoded manifest
- tools/       : scripts (run with .venv/bin/python -I, venv has UnityPy + TypeTreeGeneratorAPI)

## Findings
- AssetRipper 2.0 (nixpkgs) does NOT work: Unity 3.4 stripped files => every asset "Unreadable".
- UnityPy reads all built-in types (GameObject 28490, Mesh 638, Texture2D 1430, AudioClip 465,
  AnimationClip 922, Material 770, Shader 108 ...). With TypeTreeGenerator("3.4.2f3") over Managed/
  4735/4736 MonoBehaviours read OK => full scene/prefab reconstruction is possible (custom converter).
- Shaders: compiled GLSL ES inside ShaderLab; custom "Griptonite/*" ones need an HLSL rewrite.
- Unity 6 blockers: legacy particles (444 emitters -> ParticleSystem), GUIText(24)/GUILayer, 3.x API
  (.active, .renderer, iPhone*, LoadLevel, WWW).
- Ads/trackers (to drop in port): Burstly (AdMob, InMobi, Millennial, Greystripe), Amazon ads, Flurry,
  Tapjoy, PlayHaven, WildTangent BrandBoost, Glu GWallet (libgwallet.so), Google/Amazon IAP, LVL.
  No malware found: no unknown packages, no dynamic dex loading, native libs have no URLs/shell.
- Never use `pkill -f AssetRipper` in a bash call (it matches and kills the calling shell).

## Godot port (2026-11-17) - WORKS on an arm64-only phone (Snapdragon 8s Gen 3)
Approach: decompiled Unity 3.4 UnityEngine.dll -> `godot/src/UnityEngine` (icall externs turned into C# 13
`partial` members); icalls implemented in `godot/src/Engine` on top of Godot 4.7 mono (.NET 9, GL Compatibility).
Game code (decompiled) in `godot/src/Game`; ads/trackers/IAP replaced by inert stubs (`Assembly-CSharp-firstpass/NoAds.cs`),
online version check / asset bundles / JNI removed. Data: `tools/export_assets.py` (UnityPy) -> godot/data, tex, audio.

Workflow (inside `nix develop`):
- `tools/stubs.sh` after adding icall implementations (regenerates godot/src/Engine/Stubs.g.cs)
- `tools/run_pc.sh [--headless] [--seconds N] [--verbose] [--level=N]` - run on PC
- `tools/shot_pc.sh "4,9" --tap="5:0.5:0.75" [--dump=8]` - offscreen (Xvfb/llvmpipe, muted) screenshots -> build/shots
- build everything: `tools/build.sh [--install] [--reexport]` (see README.md)
- some phones (MIUI/HyperOS) ask to confirm adb installs on the screen.
- package com.mesflit.svz; launch toast "MADE BY MESFLIT" (Engine/Core/Toast.cs, native via AndroidRuntime);
  "Other Games" buttons removed (MainMenuImpl, StoreImpl); Options > About opens https://github.com/mesflit/svz
  (OpenURL only allows github.com/mesflit/).

Unity 3.4 quirks learned:
- scene 0 lives in mainData, scene N in level(N-1)
- every GameObject inside asset files (prefabs) has m_IsActive=0 -> treat as active
- MonoBehaviour m_Script: TypeTreeGenerator misaligns it; exporter reads it from raw bytes @12
- streamed music: small serialized file + .streamingResourceImage (raw mp3)
- static batching: renderers draw m_SubsetIndices of a "Combined Mesh" with the batch root transform
- Z-mirror conversion (S*M*S) already gives Godot front faces: do NOT flip triangle winding
- .NET CryptoStream returns partial reads (save file decrypt fixed with ReadFully)
- force InvariantCulture (Turkish locale breaks float parsing)

TODO: legacy particles (Particles.Update is a stub), OnGUI/ImGui (stub), hide "Other Games" buttons,
Pachinko physics verification, APK size (301 MB), release signing.
