#!/usr/bin/env bash
# Build the C# project and run the game on the PC. Extra args go to the game (e.g. --verbose --level=1).
# usage (inside `nix develop`): tools/run_pc.sh [--headless] [--seconds N] [game args...]
set -e
cd "$(dirname "$0")/../godot"
headless=""; secs=0; args=()
while [ $# -gt 0 ]; do
  case "$1" in
    --headless) headless="--headless";;
    --seconds) secs="$2"; shift;;
    *) args+=("$1");;
  esac; shift
done
dotnet build -v q -nologo 2>&1 | grep -E " error |rror\(s\)" | grep -v " 0 " || true
if [ "$secs" != 0 ]; then
  timeout "$secs" godot $headless -- "${args[@]}" || true
else
  godot $headless -- "${args[@]}"
fi
