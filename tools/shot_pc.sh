#!/usr/bin/env bash
# Render the game off-screen (Xvfb + Mesa llvmpipe) and save screenshots.
# usage (inside nix develop): tools/shot_pc.sh "2,6,10" [game args...]   -> build/shots/shot_<sec>.png
set -e
cd "$(dirname "$0")/.."
shots="$1"; shift
(cd godot && dotnet build -v q -nologo 2>&1 | grep -E " error " || true)
ud="$PWD/.nixenv/share/godot/app_userdata/SvZ Defense"
mkdir -p build/shots
export __GLX_VENDOR_LIBRARY_NAME=mesa LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
timeout 300 nix shell nixpkgs#xvfb-run -c xvfb-run -a -s "-screen 0 1280x720x24" \
  godot --path godot --audio-driver Dummy --rendering-driver opengl3 --resolution 1280x720 -- --shots="$shots" "$@" 2>&1 | grep -vE "^\s*$" || true
cp "$ud"/shot_*.png build/shots/ 2>/dev/null && rm -f "$ud"/shot_*.png; ls build/shots
