#!/usr/bin/env bash
# Emulator smoke test for a built SvZ Enhanced APK (run with an emulator attached, e.g. in CI).
# This is an EMULATOR result, not a phone result: it shows the APK installs, starts, loads the game and
# runs the frame-pacing code. It does not measure 120 FPS.
#
#   emulator_smoke.sh APK PACKAGE OUTDIR [SECONDS]
# Writes OUTDIR/smoke.txt (first line PASS/FAIL + reasons), logcat.txt, screenshot.png. Exit 0 = PASS.
set -uo pipefail
apk="$1"; pkg="$2"; out="$3"; wait_s="${4:-60}"
mkdir -p "$out"
fail=(); note() { echo "$*" >> "$out/details.txt"; }
: > "$out/details.txt"

adb wait-for-device
abi=$(adb shell getprop ro.product.cpu.abilist | tr -d '\r'); note "emulator ABIs: $abi"
if ! adb install -r --no-incremental "$apk" > "$out/install.txt" 2>&1; then
  fail+=("install failed: $(tail -1 "$out/install.txt")")
else
  adb logcat -c
  adb shell monkey -p "$pkg" -c android.intent.category.LAUNCHER 1 > /dev/null 2>&1
  sleep "$wait_s"
  pid=$(adb shell pidof "$pkg" | tr -d '\r')
  adb exec-out screencap -p > "$out/screenshot.png" 2>/dev/null || rm -f "$out/screenshot.png"
  adb logcat -d > "$out/logcat.txt"
  [ -n "$pid" ] || fail+=("app not running after ${wait_s}s")
  grep -q "\[svz-fps\] settings:" "$out/logcat.txt" || fail+=("no [svz-fps] start line (SvzEnhanced.dll not loaded?)")
  grep -q "\[svz\] data:" "$out/logcat.txt" || fail+=("game data did not load ([svz] data: line missing)")
  if grep -qE "FATAL EXCEPTION|Could not load file or assembly|Failed to load assembly|MissingMethodException|TypeLoadException" "$out/logcat.txt"; then
    fail+=("errors in logcat: $(grep -m1 -E 'FATAL EXCEPTION|Could not load file or assembly|Failed to load assembly|MissingMethodException|TypeLoadException' "$out/logcat.txt" | cut -c1-200)")
  fi
  grep "\[svz-fps\]\|\[svz\] data:\|\[svz\] level" "$out/logcat.txt" | sed 's/^.*\(\[svz\)/\1/' | head -20 >> "$out/details.txt"
fi

{
  if [ ${#fail[@]} -eq 0 ]; then echo "PASS: installed, started, game data loaded, frame pacing active (emulator)"
  else echo "FAIL: ${fail[*]}"; fi
  cat "$out/details.txt"
} > "$out/smoke.txt"
cat "$out/smoke.txt"
[ ${#fail[@]} -eq 0 ]
