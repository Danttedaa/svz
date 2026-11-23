#!/usr/bin/env bash
# Release signing key for the private SvZ Enhanced build, kept in the repository only encrypted.
#
#   ci/signing.sh generate KEYFILE.enc       create a new key (only when none exists), print its certificate SHA-256
#   ci/signing.sh decrypt  KEYFILE.enc OUT   decrypt for one build, print its certificate SHA-256
#
# The passphrase comes from the environment variable SVZ_KEY_PASSPHRASE (a GitHub Actions secret that only
# the repository owner sets; nobody can read it back). It is both the PKCS12 password and the key of an outer
# AES-256 layer (PBKDF2-SHA256, 600,000 iterations). Without the passphrase the .enc file is useless, so the
# file may live in the private repository and in backups; keep the passphrase in your password manager.
#
# Restore on any computer (OpenSSL 1.1.1+):
#   openssl enc -d -aes-256-cbc -pbkdf2 -iter 600000 -md sha256 -in release-key.p12.enc -out release-key.p12
#   (key alias "svz", keystore password = the passphrase)
set -euo pipefail
ALIAS=svz
ITER=600000
die() { echo "signing: $*" >&2; exit 1; }
[ -n "${SVZ_KEY_PASSPHRASE:-}" ] || die "SVZ_KEY_PASSPHRASE is not set"
[ ${#SVZ_KEY_PASSPHRASE} -ge 20 ] || die "passphrase too short: use at least 20 characters (a password-manager generated one)"
command -v keytool >/dev/null && command -v openssl >/dev/null || die "needs keytool and openssl"
here="$(cd "$(dirname "$0")" && pwd)"

fingerprint() {  # PKCS12 file -> certificate SHA-256 (64 hex digits)
  keytool -list -v -keystore "$1" -storetype PKCS12 -storepass:env SVZ_KEY_PASSPHRASE -alias "$ALIAS" 2>/dev/null \
    | python3 -I "$here/config.py" fingerprint
}

cmd="${1:-}"; enc="${2:-}"
case "$cmd" in
  generate)
    [ -n "$enc" ] || die "usage: generate KEYFILE.enc"
    [ ! -e "$enc" ] || die "$enc already exists: refusing to replace a signing key"
    tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
    keytool -genkeypair -keystore "$tmp/k.p12" -storetype PKCS12 -storepass:env SVZ_KEY_PASSPHRASE \
      -keypass:env SVZ_KEY_PASSPHRASE -alias "$ALIAS" -dname "CN=SvZ Enhanced" -keyalg RSA -keysize 4096 \
      -validity 36500 >/dev/null 2>&1 || die "keytool failed"
    fp=$(fingerprint "$tmp/k.p12") || die "cannot read the new key"
    mkdir -p "$(dirname "$enc")"
    openssl enc -aes-256-cbc -pbkdf2 -iter $ITER -md sha256 -salt -pass env:SVZ_KEY_PASSPHRASE \
      -in "$tmp/k.p12" -out "$enc"
    echo "$fp"
    ;;
  decrypt)
    out="${3:-}"
    [ -n "$enc" ] && [ -n "$out" ] || die "usage: decrypt KEYFILE.enc OUT.p12"
    [ -f "$enc" ] || die "$enc not found"
    (umask 077; openssl enc -d -aes-256-cbc -pbkdf2 -iter $ITER -md sha256 -pass env:SVZ_KEY_PASSPHRASE \
      -in "$enc" -out "$out" 2>/dev/null) || { rm -f "$out"; die "cannot decrypt $enc: wrong passphrase?"; }
    fingerprint "$out" || { rm -f "$out"; die "decrypted file is not a readable keystore: wrong passphrase?"; }
    ;;
  *) die "usage: generate KEYFILE.enc | decrypt KEYFILE.enc OUT.p12";;
esac
