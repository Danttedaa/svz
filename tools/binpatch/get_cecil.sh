#!/usr/bin/env bash
# Fetch Mono.Cecil 0.11.6 source (pinned commit) into tools/binpatch/.cecil
set -euo pipefail
dir="$(cd "$(dirname "$0")" && pwd)/.cecil"
commit=5de7d8cbc91f6fd98dcc24b26b8c398db497809c   # tag 0.11.6 (peeled)
if [ "$(git -C "$dir" rev-parse HEAD 2>/dev/null || true)" != "$commit" ]; then
  rm -rf "$dir"
  git clone -q --depth 1 --branch 0.11.6 https://github.com/jbevain/cecil.git "$dir"
  [ "$(git -C "$dir" rev-parse HEAD)" = "$commit" ] || { echo "cecil tag 0.11.6 is not $commit" >&2; exit 1; }
fi
echo "$dir"
