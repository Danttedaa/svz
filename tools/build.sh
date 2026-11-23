#!/usr/bin/env bash
# Build the arm64 APK from scratch.
#
#   nix develop -c tools/build.sh [options]
#
#   --apk PATH     your copy of the original Samurai vs Zombies Defense 3.4.0 APK (default: original/svz.apk)
#   --obb PATH     its expansion file (main.*.obb) if the game data is not merged into the APK
#   --reexport     re-extract, re-decompile and re-convert everything from the APK
#   --debug        debug export (default: release, signed with keys/svz.jks)
#   --platform P   android (default) | windows | linux | linux-arm64 | macos | all (comma separated works too)
#   --install      install on the phone with adb afterwards (android)
#   --package ID        Android package id (default: the preset's, com.mesflit.svz)
#   --app-name NAME     launcher name
#   --version-code N    Android versionCode; must grow with every update you install over a previous build
#   --version-name S    shown in Android's app info
#
# Signing (release): the Nix shell points Godot at keys/svz.jks (password "android") and creates that key
# if it is missing. These environment variables override it (CI uses them):
#   SVZ_KEYSTORE, SVZ_KEYSTORE_ALIAS, SVZ_KEYSTORE_PASSWORD
#   SVZ_EXPECT_CERT_SHA256   refuse to sign unless the key's certificate has this SHA-256 fingerprint
#                            (protects against silently signing with a new key: Android would then refuse
#                            to update the installed app, and reinstalling deletes the save game)
#
# Output: build/svz.apk, build/svz-windows.zip, build/svz-linux-x86_64.zip, build/svz-linux-arm64.zip, build/svz-macos.zip
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

apk="$root/original/svz.apk"; obb=""
reexport=0; mode=release; install=0; platforms=android
pkg=""; app_name=""; version_code=""; version_name=""
while [ $# -gt 0 ]; do
  case "$1" in
    --apk) apk="$2"; shift;;
    --obb) obb="$2"; shift;;
    --reexport) reexport=1;;
    --debug) mode=debug;;
    --install) install=1;;
    --platform) platforms="$2"; shift;;
    --package) pkg="$2"; shift;;
    --app-name) app_name="$2"; shift;;
    --version-code) version_code="$2"; shift;;
    --version-name) version_name="$2"; shift;;
    -h|--help) sed -n 2,28p "$0"; exit 0;;
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
  unzip -q -o "$apk" 'assets/*.mp4' -d "$root/work/apk" 2>/dev/null || true
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
  # full-screen movies: Godot plays Theora, the game shipped mp4
  rm -rf godot/video; mkdir -p godot/video
  for m in "$root"/work/apk/assets/*.mp4; do
    [ -f "$m" ] || continue
    case "$(basename "$m")" in Glu_logo_*) continue;; esac   # never played by the Android build
    ffmpeg -loglevel error -y -i "$m" -c:v libtheora -q:v 6 -c:a libvorbis -q:a 4 "godot/video/$(basename "${m%.mp4}").ogv"
  done
else
  step "2/5 game data already converted (godot/data)"
fi

step "3/5 regenerating icall stubs"
tools/stubs.sh

step "4/5 importing assets and compiling C#"
(cd godot && godot --headless --import >/dev/null 2>&1 || true)
(cd godot && dotnet build -v q -nologo | grep -E " error " || true)

[ "$platforms" = all ] && platforms=android,windows,linux,linux-arm64,macos
mkdir -p build

# Android identity/version for this build only: the preset file is restored afterwards.
presets="$root/godot/export_presets.cfg"
if [ -n "$pkg$app_name$version_code$version_name" ]; then
  cp "$presets" "$presets.orig"
  trap 'mv -f "$presets.orig" "$presets"' EXIT
  args=()
  [ -z "$pkg" ] || args+=(--package "$pkg")
  [ -z "$app_name" ] || args+=(--app-name "$app_name")
  [ -z "$version_code" ] || args+=(--version-code "$version_code")
  [ -z "$version_name" ] || args+=(--version-name "$version_name")
  python3 -I tools/set_android_preset.py "$presets" "${args[@]}"
fi

# Release signing key: explicit settings win over the Nix shell defaults.
if [ -n "${SVZ_KEYSTORE:-}" ]; then export GODOT_ANDROID_KEYSTORE_RELEASE_PATH="$SVZ_KEYSTORE"; fi
if [ -n "${SVZ_KEYSTORE_ALIAS:-}" ]; then export GODOT_ANDROID_KEYSTORE_RELEASE_USER="$SVZ_KEYSTORE_ALIAS"; fi
if [ -n "${SVZ_KEYSTORE_PASSWORD:-}" ]; then export GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD="$SVZ_KEYSTORE_PASSWORD"; fi
if [ -n "${SVZ_EXPECT_CERT_SHA256:-}" ] && [ $mode = release ] && [[ ",$platforms," == *",android,"* ]]; then
  ks="${GODOT_ANDROID_KEYSTORE_RELEASE_PATH:-}"
  [ -f "$ks" ] || { echo "signing key not found: $ks" >&2; exit 1; }
  got=$(keytool -list -v -keystore "$ks" -storepass:env GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD \
          -alias "$GODOT_ANDROID_KEYSTORE_RELEASE_USER" 2>/dev/null | python3 -I tools/verify/cert_fingerprint.py)
  want=$(printf '%s' "$SVZ_EXPECT_CERT_SHA256" | python3 -I tools/verify/cert_fingerprint.py --normalize)
  [ -n "$got" ] && [ "$got" = "$want" ] || { echo "signing key fingerprint ${got:-unreadable} != expected $want: refusing to sign" >&2; exit 1; }
  echo "signing key fingerprint OK ($got)"
fi
for p in ${platforms//,/ }; do
  case "$p" in
    android) preset=Android; out=build/svz.apk;;
    windows) preset=Windows; out=build/svz-windows.zip;;
    linux) preset=Linux; out=build/svz-linux-x86_64.zip;;
    linux-arm64) preset="Linux arm64"; out=build/svz-linux-arm64.zip;;
    macos) preset=macOS; out=build/svz-macos.zip;;
    *) echo "unknown platform $p" >&2; exit 1;;
  esac
  step "5/5 exporting $preset ($mode) -> $out"
  rm -f "$out"
  log="build/export-$p.log"
  (cd godot && godot --headless --export-$mode "$preset" "../$out" > "../$log" 2>&1 || true)
  grep -E "ERROR|DONE" "$log" | grep -v EditorSettings || true
  [ -f "$out" ] || { echo "export failed: $preset (full log follows)" >&2; tail -60 "$log" >&2; exit 1; }
  ls -lh "$out"
done

if [ $install = 1 ] && [ -f build/svz.apk ]; then
  step "installing on the phone (confirm the install prompt on the phone)"
  adb install --no-incremental -r build/svz.apk
fi
