#!/usr/bin/env bash
# Build the arm64 APK from scratch.
#
#   nix develop -c tools/build.sh [options]
#
#   --apk PATH     your copy of the original Samurai vs Zombies Defense 3.4.0 APK (default: original/svz.apk)
#   --obb PATH     its expansion file (main.*.obb) if the game data is not merged into the APK
#   --reexport     re-extract, re-decompile and re-convert everything from the APK
#   --debug        debug export (default: release, signed with keys/svz.jks)
#   --install      install on the phone with adb afterwards
#
# Output: build/svz.apk
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

apk="$root/original/svz.apk"; obb=""
reexport=0; mode=release; install=0
while [ $# -gt 0 ]; do
  case "$1" in
    --apk) apk="$2"; shift;;
    --obb) obb="$2"; shift;;
    --reexport) reexport=1;;
    --debug) mode=debug;;
    --install) install=1;;
    -h|--help) sed -n 2,13p "$0"; exit 0;;
    *) echo "unknown option $1" >&2; exit 1;;
  esac; shift
done

step() { printf '\n\033[1;32m==> %s\033[0m\n' "$*"; }
data="$root/work/apk/assets/bin/Data"

if [ ! -d "$data" ] || [ $reexport = 1 ]; then
  step "1/5 extracting Unity data from $apk"
  [ -f "$apk" ] || { echo "APK not found: $apk" >&2; exit 1; }
  rm -rf "$root/work/apk"; mkdir -p "$root/work/apk"
  # game data lives in the APK (merged builds) or in the OBB expansion zip
  unzip -q "${obb:-$apk}" 'assets/bin/*' -d "$root/work/apk"
  unzip -p "$apk" res/drawable-xhdpi-v4/app_icon.png > godot/icon.png   # the game's own icon
else
  step "1/5 Unity data already extracted (work/apk)"
fi

if [ ! -d godot/src/Game ] || [ ! -d godot/src/UnityEngine ] || [ $reexport = 1 ]; then
  step "1b/5 decompiling the game and UnityEngine from your APK, applying port/ changes"
  tools/prepare_sources.sh "$data"
fi

if [ ! -f godot/data/files.json ] || [ $reexport = 1 ]; then
  step "2/5 converting game data (UnityPy) -> godot/data, godot/tex, godot/audio"
  rm -rf godot/data godot/tex godot/audio
  .venv/bin/python -I tools/export_assets.py "$data" godot
else
  step "2/5 game data already converted (godot/data)"
fi

step "3/5 regenerating icall stubs"
tools/stubs.sh

step "4/5 importing assets and compiling C#"
(cd godot && godot --headless --import >/dev/null 2>&1 || true)
(cd godot && dotnet build -v q -nologo | grep -E " error " || true)

step "5/5 exporting Android APK ($mode)"
mkdir -p build
(cd godot && godot --headless --export-$mode Android ../build/svz.apk 2>&1 | grep -E "ERROR|DONE" | grep -v EditorSettings || true)
[ -f build/svz.apk ] || { echo "export failed" >&2; exit 1; }
ls -lh build/svz.apk

if [ $install = 1 ]; then
  step "installing on the phone (confirm the install prompt on the phone)"
  adb install --no-incremental -r build/svz.apk
fi
