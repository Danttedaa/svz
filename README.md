# SvZ — Samurai vs Zombies Defense on 64-bit Android

Samurai vs Zombies Defense 3.4.0 was built with Unity 3.4 and only ships 32-bit ARM code, so it does
not install on new arm64-only phones. This project runs the game's own code and data on
**Godot 4.7 (C#)** instead, producing an arm64 APK:

- the Unity 3.4 engine API is re-implemented on Godot (`godot/src/Engine`)
- the game's scripts run unchanged, decompiled from **your own copy** of the game
- ads, tracking, analytics and the Glu wallet are removed; the app has no internet permission
- the store can no longer sell anything, so every coin/gem pack in the shop is free

**This repository contains no game code and no game assets.** You need the original
Samurai vs Zombies Defense 3.4.0 APK (and its OBB if the data is not merged into the APK).

## Build

Requirements: [Nix](https://nixos.org) with flakes. Godot, .NET, the Android SDK and the Python tools come
from `flake.nix`; nothing is installed system-wide.

```sh
git clone https://github.com/mesflit/svz && cd svz
mkdir -p original && cp /path/to/SamuraiVsZombies-3.4.0.apk original/svz.apk
nix develop -c tools/build.sh                 # -> build/svz.apk  (about 2 minutes)
nix develop -c tools/build.sh --install       # build and install over adb
```

Options: `--apk PATH`, `--obb PATH` (expansion file `main.*.obb`), `--reexport`, `--debug`.

The first run creates a signing key in `keys/svz.jks`. Keep it: Android only accepts updates signed
with the same key, and reinstalling deletes the save game.

What `tools/build.sh` does:

1. extracts `assets/bin/Data` (Unity data + scripts) from your APK/OBB
2. `tools/prepare_sources.sh`: decompiles the game scripts and `UnityEngine.dll` (ILSpy) into
   `godot/src/Game` and `godot/src/UnityEngine`, then applies `port/` (removed SDKs, replacements, `game.patch`)
3. `tools/export_assets.py`: converts scenes, prefabs, meshes, textures, sounds and animations (UnityPy)
4. compiles the C# project and exports the Android APK with Godot

## Automatic builds (GitHub Actions)

`.github/workflows/build.yml` builds Android (arm64 + x86_64), Windows x86_64, Linux x86_64/arm64 and
macOS (universal) on every push to `main`; pushing a `v*` tag also publishes a release. Repository secrets:

- `SVZ_APK_URL`: link to your original APK (direct link, or a Google Drive "anyone with the link" share link)
- `SVZ_KEYSTORE_B64` (optional): `base64 -w0 keys/svz.jks`, keeps Android updates installable over your current install

`tools/build.sh --platform all` does the same locally.

## Run on a PC

```sh
nix develop -c tools/run_pc.sh                                  # window; the mouse acts as touch
nix develop -c tools/shot_pc.sh "4,9" --tap="5:0.5:0.75"        # off-screen screenshots -> build/shots
```

## Layout

| path | content |
|---|---|
| `godot/src/Engine/` | Unity 3.4 runtime on Godot: object model, lifecycle, coroutines, asset loader, rendering + shaders, GUITexture/GUIText, legacy animation, audio, physics |
| `port/` | changes to the game: `remove.txt` (ad/tracking SDKs), `overrides/` (inert replacements), `game.patch` (offline, save path, .NET fixes, no "Other Games", About link) |
| `tools/` | build pipeline, data exporter, icall stub generator |
| `NOTES.md` | technical notes: Unity 3.4 file-format quirks, status, TODO |

Generated / local only (git-ignored): `godot/src/Game`, `godot/src/UnityEngine`, `godot/data|tex|audio`,
`original/`, `work/`, `build/`, `keys/`.

## Status

Menus, story movie, shop, gameplay and legacy particle effects work. Missing: `OnGUI` screens; Pachinko is untested.

## License

The code in this repository is MIT licensed (see `LICENSE`).

## Legal

Samurai vs Zombies Defense is © Glu Mobile; Unity is © Unity Technologies. This repository only contains
original code that loads data from a copy of the game you provide. Do not distribute the generated
APK, the decompiled sources or the converted game data.
