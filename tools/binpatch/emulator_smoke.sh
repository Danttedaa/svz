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
#   3. Store original-HD prefilter (--level=3 --test-original-hd): actual HD glyph draw is required
#   4. battle opened directly (--level=2 --killall=25): skinning, animation blending, death and corpse code
# Stages 2-4 need the game's debug command line (--level=N). Godot ignores command-line extras sent to its
# exported launcher activity, so these stages install emulator-only VARIANTS of the APK whose packaged
# command line (assets/_cl_) has the arguments appended, signed with a throwaway key (smoke_variant.py).
# The published APK is not changed. If a variant cannot be built, the stage is WARN (not exercised).
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

here="$(cd "$(dirname "$0")" && pwd)"
bt=$(ls -d "${ANDROID_HOME:-/nonexistent}"/build-tools/*/ 2>/dev/null | sort -V | tail -1)
vkey="$out/variant-key.p12"

make_variant() {   # stage args... -> prints the signed variant path
  local stage="$1"; shift
  local unsigned="$out/variant-$stage-unsigned.apk" aligned="$out/variant-$stage-aligned.apk" signed="$out/variant-$stage.apk"
  [ -n "$bt" ] && [ -x "${bt}apksigner" ] && [ -x "${bt}zipalign" ] || return 1
  if [ ! -f "$vkey" ]; then
    keytool -genkeypair -keystore "$vkey" -storetype PKCS12 -storepass smoke-only -keypass smoke-only -alias smoke \
      -dname "CN=emulator smoke test only" -keyalg RSA -keysize 2048 -validity 2 > /dev/null 2>&1 || return 1
  fi
  python3 -I "$here/smoke_variant.py" "$apk" "$unsigned" -- "$@" >> "$out/details.txt" 2>&1 || return 1
  "${bt}zipalign" -p -f 4 "$unsigned" "$aligned" > /dev/null || return 1
  "${bt}apksigner" sign --ks "$vkey" --ks-pass pass:smoke-only --ks-key-alias smoke --out "$signed" "$aligned" > /dev/null 2>&1 || return 1
  rm -f "$unsigned" "$aligned" "$signed.idsig"
  echo "$signed"
}

launch_level() {   # stage level seconds args...
  local stage="$1" level="$2" secs="$3"; shift 3
  local log="$out/logcat-$stage.txt" variant
  if ! variant=$(make_variant "$stage" "$@"); then
    warn+=("$stage: could not build the emulator test variant, stage not exercised"); return 1
  fi
  adb uninstall "$pkg" > /dev/null 2>&1
  if ! adb install -r --no-incremental "$variant" > "$out/install-$stage.txt" 2>&1; then
    warn+=("$stage: test variant did not install: $(tail -1 "$out/install-$stage.txt")"); rm -f "$variant"; return 1
  fi
  rm -f "$variant"
  adb logcat -c
  adb shell monkey -p "$pkg" -c android.intent.category.LAUNCHER 1 > /dev/null 2>&1
  sleep "$secs"
  local pid; pid=$(adb shell pidof "$pkg" | tr -d '\r')
  adb exec-out screencap -p > "$out/screenshot-$stage.png" 2>/dev/null || rm -f "$out/screenshot-$stage.png"
  # Reject blank screens and system overlays instead of accepting log-only evidence.
  if [[ "$stage" == store* ]]; then
    local bytes=0
    if [ -f "$out/screenshot-$stage.png" ]; then bytes=$(wc -c < "$out/screenshot-$stage.png"); fi
    if [ "$bytes" -lt 20000 ]; then
      fail+=("$stage: screenshot too small for visual verification ($bytes bytes)")
      adb shell dumpsys window > "$out/window-$stage.txt" 2>&1
    fi
  fi
  adb logcat -d > "$log"
  [ -n "$pid" ] || fail+=("$stage: app not running after ${secs}s")
  check_errors "$stage" "$log"
  if grep -q "\[svz\] loading level $level " "$log"; then
    note "$stage: level $level loaded"
    return 0
  fi
  warn+=("$stage: level $level was not opened, stage not exercised")
  return 1
}

adb wait-for-device
# Wait for Android framework boot, not merely the ADB transport.
for ((i=0; i<90; i++)); do
  [ "$(adb shell getprop sys.boot_completed | tr -d '\r')" = 1 ] && break
  sleep 2
done
adb shell input keyevent KEYCODE_WAKEUP
adb shell wm dismiss-keyguard
adb shell svc power stayon true
# Acknowledge Android immersive-mode education on the disposable CI emulator.
adb shell settings put secure immersive_mode_confirmations confirmed >/dev/null 2>&1 || warn+=("could not acknowledge emulator fullscreen education")
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
  if [ -z "$pid" ]; then
    fail+=("app not running after ${wait_s}s")
    adb shell dumpsys window > "$out/window-start.txt" 2>&1
    adb shell dumpsys activity exit-info "$pkg" > "$out/exit-start.txt" 2>&1
    cat "$out/exit-start.txt"
  fi
  grep -q "\[svz-fps\] settings:" "$out/logcat.txt" || fail+=("no [svz-fps] start line (SvzEnhanced.dll not loaded?)")
  grep -q "\[svz\] data:" "$out/logcat.txt" || fail+=("game data did not load ([svz] data: line missing)")
  check_errors start "$out/logcat.txt"
  grep "\[svz-fps\]\|\[svz\] data:\|\[svz\] level" "$out/logcat.txt" | sed 's/^.*\(\[svz\)/\1/' | head -20 >> "$out/details.txt"

  if [ "${#fail[@]}" -eq 0 ]; then
    # stage 2: Store (fonts + contrast halo)
    if launch_level store 3 45 --level=3; then
      if grep -q "\[svz-ui\] text scale .*mode original, contrast off" "$out/logcat-store.txt"; then
        note "store: user-preferred original font initialized at 100%, no extra halo"
      else
        fail+=("store: original font default missing or font settings did not initialize")
      fi
      if grep -q "\[svz-ui\] readable font first label rendered" "$out/logcat-store.txt"; then
        fail+=("store: experimental native font drew without user selecting it")
      fi
    fi
    # Stage 3 must approximate the PHONE font-resolution class (768-base,
    # 1536-selected). The CI emulator normally exposes just 320 game pixels
    # and a 256x256 legacy font; it CANNOT exercise the HD-only filter.
    # Android's native display is portrait, while the game is landscape.
    note "store-hd: display before override $(adb shell wm size | tr -d '\r')"
    if adb shell wm size 720x1280 >/dev/null 2>&1; then
      note "store-hd: display override $(adb shell wm size | tr -d '\r')"
      sleep 3
    else
      warn+=("store-hd: Android emulator cannot set phone-size display")
    fi
    # The debug mode is packaged only in an emulator test variant.
    if launch_level store-hd 3 60 --level=3 --test-original-hd; then
      grep -q "\[svz-ui\] text scale .*mode original HD, contrast off" "$out/logcat-store-hd.txt" \
        || fail+=("store-hd: prefiltered-original A/B mode did not initialize")
      if grep -q "\[svz-ui\] prefiltered original font glyph rendered" "$out/logcat-store-hd.txt"; then
        note "store-hd: same original glyphs rendered from antialiased atlas"
      else
        fail+=("store-hd: no original-font prefiltered glyph was actually drawn")
      fi
      # A build claiming repaired glyphs must prove at least one damaged pixel
      # was changed in the actual original atlas (not merely in synthetic tests).
      if grep -Eq "\[svz-ui\] original-font atlas prefiltered: .*pinholes=[1-9][0-9]*" "$out/logcat-store-hd.txt"; then
        note "store-hd: actual original glyph atlas has repaired pinholes"
      else
        fail+=("store-hd: no alpha pinholes repaired in the real game font")
      fi
      grep -o '\[svz-ui\] font prefilter diagnostics:.*\|\[svz-ui\] original-font prefilter unavailable:.*\|\[svz-ui\] original-font atlas prefiltered:.*' \
        "$out/logcat-store-hd.txt" | head -8 | sed 's/^/store-hd: /' >> "$out/details.txt" || true
    fi
    # Compare the same store at the same display size without forced HD selection.
    if launch_level store-native 3 60 --level=3 --test-native-font-baseline; then
      grep -Fq "[svz-ui] native font baseline: nearest=768 selected=768" "$out/logcat-store-native.txt" || fail+=("store-native: native resolution not verified")
    else
      fail+=("store-native: baseline not exercised")
    fi
    adb shell wm size reset >/dev/null 2>&1 || warn+=("store-hd: could not restore emulator display size")
    # stage 4: battle (skinning, animation, deaths); --killall kills every enemy after 25 s
    if launch_level battle 2 60 --level=2 --killall=30; then
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
# Only on failure, surface the actual Android process-exit evidence in GitHub's
# private workflow log; release assets keep full logcat for deeper diagnosis.
# Do not hide a crash by relaunching or marking the build as verified.
if [ ${#fail[@]} -ne 0 ] && [ -s "$out/logcat.txt" ]; then
  echo "=== Android startup failure diagnostics (last matching lines) ==="
  grep -Ei 'FATAL EXCEPTION|AndroidRuntime|Godot|svz|am_crash|am_kill|am_proc_died|ActivityManager|lowmemorykiller|OutOfMemory|SIGSEGV|signal 11|ANR|Force finishing|Force stopping' "$out/logcat.txt" \
    | tail -n 90 || true
fi
[ ${#fail[@]} -eq 0 ]
