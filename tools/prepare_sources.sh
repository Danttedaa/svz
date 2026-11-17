#!/usr/bin/env bash
# Recreate the decompiled sources from the user's own copy of the game:
#   godot/src/UnityEngine  <- UnityEngine.dll (Unity 3.4.2), externs turned into partial members
#   godot/src/Game         <- Assembly-CSharp*.dll + port/ (removed SDKs, overrides, game.patch)
# usage (inside nix develop): tools/prepare_sources.sh <Unity Data dir> [out dir (default godot/src)]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
data="$(realpath "$1")"; out="${2:-$root/godot/src}"
managed="$data/Managed"
tmp="$root/work/decompile"
rm -rf "$tmp"; mkdir -p "$tmp"

decompile() {  # dll -> dir
  "$SVZ_ILSPYCMD" -p -o "$2" "$1" >/dev/null
  rm -rf "$2/Properties" "$2"/*.csproj
}

echo "decompiling UnityEngine.dll"
decompile "$managed/UnityEngine.dll" "$tmp/UnityEngine"
rm -rf "$out/UnityEngine"
"$root/.venv/bin/python" -I "$root/tools/import_unityengine.py" "$tmp/UnityEngine" "$out/UnityEngine" >/dev/null
# [SecurityCritical] is not valid on properties in C#
grep -rl '\[SecurityCritical\]' "$out/UnityEngine" | xargs -r sed -i '/^\s*\[SecurityCritical\]\s*$/d'

echo "decompiling game assemblies"
rm -rf "$out/Game"; mkdir -p "$out/Game"
for a in Assembly-CSharp-firstpass Assembly-CSharp Assembly-UnityScript; do
  decompile "$managed/$a.dll" "$out/Game/$a"
done

echo "applying port changes (port/)"
cd "$out/Game"
# ad / tracking / wallet / IAP SDKs and their glue
grep -vE '^\s*(#|$)' "$root/port/remove.txt" | while read -r p; do rm -rf -- "$p"; done
find . -type d -empty -delete
cp -r "$root/port/overrides/." .
patch -p1 --no-backup-if-mismatch -s < "$root/port/game.patch"
echo "sources ready in $out"
