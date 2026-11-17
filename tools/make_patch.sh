#!/usr/bin/env bash
# After editing godot/src/Game, regenerate port/game.patch (and port/overrides) from the differences
# against a fresh decompile. Files that exist only in the port, or that are listed in port/overrides,
# are copied whole; all other changes go into the patch.
# usage (inside nix develop): tools/make_patch.sh [Unity Data dir (default work/apk/assets/bin/Data)]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"; cd "$root"
data="${1:-$root/work/apk/assets/bin/Data}"
pristine="$root/work/pristine/Game"; cur="$root/godot/src/Game"
rm -rf "$pristine"; mkdir -p "$pristine"
for a in Assembly-CSharp-firstpass Assembly-CSharp Assembly-UnityScript; do
  "$SVZ_ILSPYCMD" -p -o "$pristine/$a" "$data/Managed/$a.dll" >/dev/null
  rm -rf "$pristine/$a/Properties" "$pristine/$a"/*.csproj
done
(cd "$cur" && find . -name '*.cs' | sed 's|^\./||' | sort) > work/cur.txt
(cd "$pristine" && find . -name '*.cs' | sed 's|^\./||' | sort) > work/pristine.txt
(cd port/overrides && find . -name '*.cs' | sed 's|^\./||' | sort) > work/ovr.txt
# overrides: new files + files already marked as overrides
{ comm -13 work/pristine.txt work/cur.txt; cat work/ovr.txt; } | sort -u > work/ovr_new.txt
rm -rf port/overrides; mkdir -p port/overrides
while read -r f; do mkdir -p "port/overrides/$(dirname "$f")"; cp "$cur/$f" "port/overrides/$f"; done < work/ovr_new.txt
# patch: changed files that are not overrides
: > port/game.patch
comm -12 work/pristine.txt work/cur.txt | comm -23 - work/ovr_new.txt | while read -r f; do
  diff -U2 --label "a/$f" --label "b/$f" "$pristine/$f" "$cur/$f" >> port/game.patch || true
done
echo "overrides: $(wc -l < work/ovr_new.txt) files, patch: $(grep -c '^+++ ' port/game.patch) files"
echo "removed files are listed in port/remove.txt (edit by hand)"
