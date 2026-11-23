#!/usr/bin/env bash
# Signing key step shared by the private workflows. Needs SVZ_KEY_PASSPHRASE, KEYFILE, STATUS (markdown file),
# RUNNER_TEMP, GITHUB_OUTPUT; run from the repository root (a branch checkout, so the pin can be pushed).
# Outputs: ready=true|false, cert=<SHA-256>; the decrypted key is at $RUNNER_TEMP/key/release.p12.
set -euo pipefail
pinned=$(python3 -I ci/config.py get cert_sha256)
if [ -z "${SVZ_KEY_PASSPHRASE:-}" ]; then
  echo "- ⏳ **Waiting for you:** add the secret \`SVZ_KEY_PASSPHRASE\` (Settings → Secrets and variables → Actions)." >> "$STATUS"
  echo "ready=false" >> "$GITHUB_OUTPUT"; exit 0
fi
if [ ! -f "$KEYFILE" ]; then
  if [ -n "$pinned" ]; then
    echo "::error::$KEYFILE is missing but a certificate is pinned. Restore it from your backup; a new key would break updates."
    echo "- ❌ Signing key file missing although a key is pinned: restore \`$KEYFILE\` from your backup." >> "$STATUS"
    exit 1
  fi
  fp=$(bash ci/signing.sh generate "$KEYFILE")
  python3 -I ci/config.py set cert_sha256 "$fp"
  git add "$KEYFILE" svz-build.json
  git commit -q -m "Create release signing key (encrypted) and pin its certificate"
  git push -q
  echo "- 🔑 Created your release signing key (stored encrypted in \`$KEYFILE\`). **Back it up:** download that file and keep it with the passphrase in your password manager." >> "$STATUS"
  pinned="$fp"
fi
(umask 077; mkdir -p "$RUNNER_TEMP/key")
got=$(bash ci/signing.sh decrypt "$KEYFILE" "$RUNNER_TEMP/key/release.p12") || {
  echo "- ❌ Could not decrypt the signing key: is \`SVZ_KEY_PASSPHRASE\` the passphrase it was created with?" >> "$STATUS"; exit 1; }
[ "$got" = "$pinned" ] || { echo "::error::signing key certificate $got != pinned $pinned"; exit 1; }
echo "- ✅ Signing key OK (certificate \`${got:0:16}…\`)" >> "$STATUS"
echo "ready=true" >> "$GITHUB_OUTPUT"
echo "cert=$got" >> "$GITHUB_OUTPUT"
