#!/usr/bin/env bash
# One-time setup of the private SvZ Enhanced build repository. Run it on YOUR computer
# (Linux, macOS or WSL on Windows), from the root of this repository:
#
#   tools/private_build_setup.sh --repo Danttedaa/svz-private --apk /path/to/original.apk [options]
#
# It needs: gh (logged in: gh auth login), keytool (any JDK 17+), python3, unzip.
#
#   --repo OWNER/NAME     the PRIVATE repository that builds and stores the APKs (must already exist)
#   --apk PATH            your legitimately obtained original Samurai vs Zombies Defense 3.4.0 APK
#   --obb PATH            its expansion file (main.*.obb), only if the game data is not inside the APK
#   --keystore PATH       an existing release keystore to keep using (password: env SVZ_KEYSTORE_PASSWORD
#                         or a prompt; alias: --alias, default svz); default: create a new one
#   --alias NAME          key alias (default svz)
#   --engine-repo R       public engine repository (default Danttedaa/svz)
#   --engine-ref REF      branch/tag to build (default main)
#   --package ID          Android package id (default io.github.danttedaa.svz)
#   --no-run              do not start the first build
#
# What it does:
#   1. refuses to continue unless --repo is private
#   2. creates a release signing key in ~/.svz-signing/ (mode 600) unless --keystore is given.
#      BACK UP that folder offline: Android only installs updates signed with the same key; without it
#      you would have to uninstall the game, which deletes the save game.
#   3. stores the key as repository secrets (SVZ_KEYSTORE_B64/PASSWORD/ALIAS) via stdin, never on a command line
#   4. uploads the original APK (and OBB) once as the private release "original" and records its SHA-256
#   5. sets the repository variables and installs .github/workflows/build-apk.yml from ci/private/
#   6. starts the first build (Actions tab of the private repository; about 30-60 minutes)
# Re-running is safe: existing key secrets are replaced only when a key is created or given, and the
# original files are re-uploaded only if their SHA-256 changed.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"

repo=""; apk=""; obb=""; keystore=""; alias_name="svz"; engine_repo="Danttedaa/svz"; engine_ref="main"
package="io.github.danttedaa.svz"; run=1
while [ $# -gt 0 ]; do
  case "$1" in
    --repo) repo="$2"; shift;;
    --apk) apk="$2"; shift;;
    --obb) obb="$2"; shift;;
    --keystore) keystore="$2"; shift;;
    --alias) alias_name="$2"; shift;;
    --engine-repo) engine_repo="$2"; shift;;
    --engine-ref) engine_ref="$2"; shift;;
    --package) package="$2"; shift;;
    --no-run) run=0;;
    -h|--help) sed -n 2,33p "$0"; exit 0;;
    *) echo "unknown option $1" >&2; exit 2;;
  esac; shift
done
die() { echo "error: $*" >&2; exit 1; }
step() { printf '\n==> %s\n' "$*"; }
b64() { base64 -w0 "$1" 2>/dev/null || base64 < "$1" | tr -d '\n'; }
sha256() { python3 -I -c 'import hashlib,sys; print(hashlib.sha256(open(sys.argv[1],"rb").read()).hexdigest())' "$1"; }

[ -n "$repo" ] && [ -n "$apk" ] || die "--repo and --apk are required (see --help)"
for t in gh keytool python3 unzip; do command -v "$t" >/dev/null || die "$t not found"; done
[ -f "$apk" ] || die "APK not found: $apk"
[ -z "$obb" ] || [ -f "$obb" ] || die "OBB not found: $obb"
python3 -I "$root/tools/set_android_preset.py" --help >/dev/null
printf '%s' "$package" | grep -Eq '^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$' || die "invalid package id: $package"

step "1/6 checking that $repo is private"
private=$(gh repo view "$repo" --json isPrivate -q .isPrivate) || die "cannot read $repo (does it exist? gh auth status)"
[ "$private" = true ] || die "$repo is PUBLIC. The build contains the original game: use a private repository."

step "checking the original game files"
unzip -tq "$apk" >/dev/null || die "$apk is not a valid APK"
if [ -z "$obb" ] && ! unzip -l "$apk" 'assets/bin/Data/*' >/dev/null 2>&1; then
  die "the APK has no assets/bin/Data (game data is in an OBB: pass --obb main.<version>.com.glu.samuzombie.obb)"
fi
apk_sha=$(sha256 "$apk"); echo "APK SHA-256 $apk_sha"
obb_sha=""; [ -z "$obb" ] || { obb_sha=$(sha256 "$obb"); echo "OBB SHA-256 $obb_sha"; }

step "2/6 release signing key"
pass_file=""
if [ -z "$keystore" ]; then
  dir="$HOME/.svz-signing"; keystore="$dir/svz-release.jks"; pass_file="$dir/svz-release.password"
  mkdir -p "$dir"; chmod 700 "$dir"
  if [ -f "$keystore" ]; then
    echo "using the existing key $keystore"
  else
    umask 077
    python3 -I -c 'import secrets; print(secrets.token_urlsafe(32))' > "$pass_file"
    SVZ_PW="$(cat "$pass_file")" keytool -genkeypair -keystore "$keystore" -storetype PKCS12 \
      -storepass:env SVZ_PW -keypass:env SVZ_PW -alias "$alias_name" \
      -dname "CN=SvZ Enhanced" -keyalg RSA -keysize 4096 -validity 36500 >/dev/null
    echo "created $keystore"
    echo "!! Back up $dir (both files) somewhere offline. Without them, updates need an uninstall = save game lost."
  fi
  SVZ_KEYSTORE_PASSWORD="$(cat "$pass_file")"
elif [ -z "${SVZ_KEYSTORE_PASSWORD:-}" ]; then
  read -rsp "password of $keystore: " SVZ_KEYSTORE_PASSWORD; echo
fi
export SVZ_KEYSTORE_PASSWORD
cert=$(keytool -list -v -keystore "$keystore" -storepass:env SVZ_KEYSTORE_PASSWORD -alias "$alias_name" 2>/dev/null \
       | python3 -I "$root/tools/verify/cert_fingerprint.py") || die "cannot read $keystore (wrong password or alias?)"
echo "certificate SHA-256 $cert"

step "3/6 storing the key as secrets of $repo"
b64 "$keystore" | gh secret set SVZ_KEYSTORE_B64 --repo "$repo"
printf '%s' "$SVZ_KEYSTORE_PASSWORD" | gh secret set SVZ_KEYSTORE_PASSWORD --repo "$repo"
printf '%s' "$alias_name" | gh secret set SVZ_KEYSTORE_ALIAS --repo "$repo"

step "4/6 original game files -> private release \"original\""
current=$(gh release view original --repo "$repo" --json body -q .body 2>/dev/null || true)
if printf '%s' "$current" | grep -q "$apk_sha"; then
  echo "already uploaded (same SHA-256)"
else
  tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
  cp "$apk" "$tmp/svz-original.apk"; files=("$tmp/svz-original.apk")
  [ -z "$obb" ] || { cp "$obb" "$tmp/svz-original.obb"; files+=("$tmp/svz-original.obb"); }
  gh release delete original --repo "$repo" --yes --cleanup-tag >/dev/null 2>&1 || true
  gh release create original --repo "$repo" --title "Original game files (private: never publish)" \
    --notes "Owner's legitimately obtained copy, input for the private SvZ Enhanced build. APK SHA-256 $apk_sha${obb_sha:+, OBB SHA-256 $obb_sha}" \
    "${files[@]}"
fi

step "5/6 repository variables and workflow"
gh variable set SVZ_ORIGINAL_SHA256 --repo "$repo" --body "$apk_sha"
gh variable set SVZ_CERT_SHA256 --repo "$repo" --body "$cert"
gh variable set SVZ_ENGINE_REPO --repo "$repo" --body "$engine_repo"
gh variable set SVZ_ENGINE_REF --repo "$repo" --body "$engine_ref"
gh variable set SVZ_PACKAGE --repo "$repo" --body "$package"
path=.github/workflows/build-apk.yml
existing=$(gh api "repos/$repo/contents/$path" -q .sha 2>/dev/null || true)
args=(-X PUT "repos/$repo/contents/$path" -f message="SvZ Enhanced private build workflow" -f content="$(b64 "$root/ci/private/build-apk.yml")")
[ -z "$existing" ] || args+=(-f sha="$existing")
gh api "${args[@]}" >/dev/null
echo "installed $path"

if [ $run = 1 ]; then
  step "6/6 starting the first build"
  for i in 1 2 3 4 5 6; do
    gh workflow run build-apk.yml --repo "$repo" -f engine_ref="$engine_ref" && break
    sleep 10   # a newly added workflow takes a moment to register
  done
  echo "follow it: gh run watch --repo $repo   (or the Actions tab). The APK appears under Releases of $repo."
fi
echo
echo "done. Certificate SHA-256: $cert"
