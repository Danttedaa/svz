#!/usr/bin/env bash
# Emulator smoke test for a built SvZ Enhanced APK (run with an emulator attached, e.g. in CI).
# This is an EMULATOR result, not a phone result: it shows the APK installs, starts, loads the game and
# runs the enhancement code. It does not measure 120 FPS.
#
#   emulator_smoke.sh APK PACKAGE OUTDIR [SECONDS]
# Writes OUTDIR/smoke.txt (first line PASS/FAIL + reasons), logcat*.txt, screenshot*.png. Exit 0 = PASS.
#
# Stages:
#   1. normal start (intro): app runs, SvzEnhanced.dll loaded, game data loaded
#   2. Store opened directly (--level=3): the text patches run (fonts, contrast halo)
#   3. battle opened directly (--level=2 --killall=25): skinning, animation blending, death and corpse code
# Stages 2-3 pass the game's own debug command line through Godot's "command_line_params" intent extra.
# If the build ignores it, they are reported as WARN (not exercised), never as PASS.
set -uo pipefail
apk="$1"; pkg="$2"; out="$3"; wait_s="${4:-60}"
mkdir -p "$out"
fail=(); warn=(); note() { echo "$*" >> "$out/details.txt"; }
: > "$out/details.txt"
# managed errors that mean a patched call site or the enhancement assembly does not work on Android Mono
bad='FATAL EXCEPTION|Could not load file or assembly|Failed to load assembly|MissingMethodException|MissingFieldException|TypeLoadException|MethodAccessException|FieldAccessException|InvalidProgramException|BadImageFormatException|VerificationException'

check_errors() {   # stage logfile
  if grep -qE "$bad" "$2"; then
    fail+=("$1: $(grep -m1 -E "$bad" "$2" | cut -c1-220)")
  fi
  local n; n=$(grep -c "\[svz\] exception in" "$2" || true)
  [ "$n" = 0 ] || note "$1: $n '[svz] exception in' lines (first: $(grep -m1 '\[svz\] exception in' "$2" | cut -c1-200))"
}

launch_level() {   # stage level extra-args seconds
  local stage="$1" level="$2" extra="$3" secs="$4" log="$out/logcat-$1.txt"
  adb shell am force-stop "$pkg"
  adb logcat -c
  adb shell am start -n "$component" --esa command_line_params "--,--level=$level$extra" > "$out/start-$stage.txt" 2>&1
  sleep "$secs"
  local pid; pid=$(adb shell pidof "$pkg" | tr -d '\r')
  adb exec-out screencap -p > "$out/screenshot-$stage.png" 2>/dev/null || rm -f "$out/screenshot-$stage.png"
  adb logcat -d > "$log"
  [ -n "$pid" ] || fail+=("$stage: app not running after ${secs}s")
  check_errors "$stage" "$log"
  if grep -q "\[svz\] loading level $level " "$log"; then
    note "$stage: level $level loaded"
    return 0
  fi
  warn+=("$stage: level $level was not opened (command_line_params ignored?), stage not exercised")
  return 1
}

adb wait-for-device
abi=$(adb shell getprop ro.product.cpu.abilist | tr -d '\r'); note "emulator ABIs: $abi"
if ! adb install -r --no-incremental "$apk" > "$out/install.txt" 2>&1; then
  fail+=("install failed: $(tail -1 "$out/install.txt")")
else
  # stage 1: normal start
  adb logcat -c
  adb shell monkey -p "$pkg" -c android.intent.category.LAUNCHER 1 > /dev/null 2>&1
  sleep "$wait_s"
  pid=$(adb shell pidof "$pkg" | tr -d '\r')
  adb exec-out screencap -p > "$out/screenshot.png" 2>/dev/null || rm -f "$out/screenshot.png"
  adb logcat -d > "$out/logcat.txt"
  [ -n "$pid" ] || fail+=("app not running after ${wait_s}s")
  grep -q "\[svz-fps\] settings:" "$out/logcat.txt" || fail+=("no [svz-fps] start line (SvzEnhanced.dll not loaded?)")
  grep -q "\[svz\] data:" "$out/logcat.txt" || fail+=("game data did not load ([svz] data: line missing)")
  check_errors start "$out/logcat.txt"
  grep "\[svz-fps\]\|\[svz\] data:\|\[svz\] level" "$out/logcat.txt" | sed 's/^.*\(\[svz\)/\1/' | head -20 >> "$out/details.txt"

  component=$(adb shell cmd package resolve-activity --brief -c android.intent.category.LAUNCHER "$pkg" | tr -d '\r' | tail -1)
  note "launcher activity: $component"
  if [ -n "$component" ] && [ "${#fail[@]}" -eq 0 ]; then
    # stage 2: Store (fonts + contrast halo)
    if launch_level store 3 "" 40; then
      grep -q "\[svz-ui\] text scale" "$out/logcat-store.txt" && note "store: text settings loaded ($(grep -m1 -o '\[svz-ui\].*' "$out/logcat-store.txt"))" \
        || warn+=("store: no [svz-ui] line (no GUI text drawn?)")
    fi
    # stage 3: battle (skinning, animation, deaths); --killall kills every enemy after 25 s
    if launch_level battle 2 ",--killall=25" 50; then
      grep -q "\[svz\] killed" "$out/logcat-battle.txt" && note "battle: $(grep -m1 -o '\[svz\] killed.*' "$out/logcat-battle.txt")"
      grep -o "\[svz-fps\] stats.*" "$out/logcat-battle.txt" | tail -1 | sed 's/^/battle: /' >> "$out/details.txt"
      grep -o "\[svz-death\].*" "$out/logcat-battle.txt" | head -3 | sed 's/^/battle: /' >> "$out/details.txt"
    fi
  fi
fi

{
  if [ ${#fail[@]} -eq 0 ]; then echo "PASS: installed, started, game data loaded, frame pacing active (emulator)"
  else echo "FAIL: ${fail[*]}"; fi
  for w in "${warn[@]}"; do echo "WARN: $w"; done
  cat "$out/details.txt"
} > "$out/smoke.txt"
cat "$out/smoke.txt"
[ ${#fail[@]} -eq 0 ]
