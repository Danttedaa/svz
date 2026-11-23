#!/usr/bin/env bash
# Build an SvZ Enhanced APK from a prebuilt SvZ port APK (e.g. the mesflit/svz release), without the original
# game APK: the port's converted game data and compiled game code are reused unchanged; only the frame-pacing
# call sites in SvZPort.dll change (tools/binpatch/SvzPatch) and SvzEnhanced.dll is added.
#
#   tools/binpatch/build_from_port.sh PORT.apk OUT.apk --ks KEYSTORE --ks-alias ALIAS --ks-pass-env VAR \
#       [--package ID] [--label NAME] [--version-code N] [--apksigner PATH] [--zipalign PATH]
#
# Needs: .NET SDK 8+, python3 with androguard, git, zipalign, apksigner. The keystore password is read from the
# environment variable named by --ks-pass-env (never passed on a command line).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
port="$1"; out="$2"; shift 2
ks=""; alias_name=""; pass_env=""; package="io.github.danttedaa.svz"; label="SvZ Enhanced"; vc="1000"
apksigner="${APKSIGNER:-apksigner}"; zipalign="${ZIPALIGN:-zipalign}"; py="${SVZ_PY:-python3}"
while [ $# -gt 0 ]; do
  case "$1" in
    --ks) ks="$2"; shift;; --ks-alias) alias_name="$2"; shift;; --ks-pass-env) pass_env="$2"; shift;;
    --package) package="$2"; shift;; --label) label="$2"; shift;; --version-code) vc="$2"; shift;;
    --apksigner) apksigner="$2"; shift;; --zipalign) zipalign="$2"; shift;;
    *) echo "unknown option $1" >&2; exit 2;;
  esac; shift
done
[ -f "$port" ] && [ -f "$ks" ] && [ -n "$alias_name" ] && [ -n "$pass_env" ] && [ -n "${!pass_env:-}" ] \
  || { echo "usage: build_from_port.sh PORT.apk OUT.apk --ks KS --ks-alias A --ks-pass-env VAR (VAR must be set)" >&2; exit 2; }
mkdir -p "$(dirname "$out")"
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
step() { printf '\n==> %s\n' "$*"; }
quiet() { local log="$work/cmd.log"; "$@" > "$log" 2>&1 || { cat "$log" >&2; echo "failed: $*" >&2; exit 1; }; }
nuget=(); [ -z "${SVZ_NUGET_CONFIG:-}" ] || nuget=(--configfile "$SVZ_NUGET_CONFIG")

step "1/6 SvzEnhanced.dll (godot/src/Engine/Core/FramePacing*.cs)"
quiet dotnet build "$here/SvzEnhanced/SvzEnhanced.csproj" -c Release -nologo -v quiet "${nuget[@]}" -o "$work/enh"
enh="$work/enh/SvzEnhanced.dll"

step "2/6 svzpatch (Mono.Cecil 0.11.6 from source)"
quiet "$here/get_cecil.sh"
quiet dotnet build "$here/SvzPatch/SvzPatch.csproj" -c Release -nologo -v quiet "${nuget[@]}" -o "$work/patcher"

step "3/6 patching SvZPort.dll"
dll_args=()
for arch in arm64 x86_64; do
  name="assets/.godot/mono/publish/$arch/SvZPort.dll"
  if "$py" -I -c "import sys, zipfile; sys.exit(0 if sys.argv[2] in zipfile.ZipFile(sys.argv[1]).namelist() else 1)" "$port" "$name"; then
    mkdir -p "$work/in/$arch" "$work/out/$arch"
    "$py" -I -c "import sys, zipfile; open(sys.argv[3], 'wb').write(zipfile.ZipFile(sys.argv[1]).read(sys.argv[2]))" "$port" "$name" "$work/in/$arch/SvZPort.dll"
    dotnet "$work/patcher/SvzPatch.dll" patch "$work/in/$arch/SvZPort.dll" "$enh" "$work/out/$arch/SvZPort.dll" > "$work/patch.log" \
      || { cat "$work/patch.log" >&2; exit 1; }
    dotnet "$work/patcher/SvzPatch.dll" check "$work/out/$arch/SvZPort.dll" "$enh" | sed "s/^/  [$arch] /"
    dll_args+=(--dll "$arch=$work/out/$arch/SvZPort.dll")
  fi
done
[ ${#dll_args[@]} -gt 0 ] || { echo "no SvZPort.dll found in $port" >&2; exit 1; }

step "4/6 repackaging (package $package, label \"$label\", versionCode $vc)"
"$py" -I "$here/repack.py" "$port" "$work/unsigned.apk" "${dll_args[@]}" --enhanced "$enh" \
  --package "$package" --label "$label" --version-code "$vc"

step "5/6 zipalign + sign"
"$zipalign" -p -f 4 "$work/unsigned.apk" "$work/aligned.apk"
"$apksigner" sign --ks "$ks" --ks-key-alias "$alias_name" --ks-pass "env:$pass_env" --out "$out" "$work/aligned.apk"
rm -f "$out.idsig"

step "6/6 verification"
"$apksigner" verify "$out"
"$py" -I "$root/tools/verify/verify_apk.py" "$out" --require-abi arm64-v8a --expect-package "$package" \
  --min-version-code "$vc" --forbid-permission android.permission.INTERNET --check-bridge --apksigner "$apksigner" \
  --json "$out.verify.json"
echo "built $out"
