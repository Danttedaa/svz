#!/usr/bin/env bash
# Off-device tests for SvZ Enhanced. Needs only a .NET 8+ SDK (and, for the runtime test, the
# official Godot 4.7.2 .NET build). Works offline: no NuGet.org access is required.
#
#   tests/run_tests.sh                         # policy tests only
#   tests/run_tests.sh /path/to/Godot_v4.7.2-stable_mono_linux_x86_64   # + headless Godot runtime test
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "== build and verification tool tests (Python)"
python3 -m unittest discover -s "$here/tools" -t "$here/tools" < /dev/null

echo "== policy tests"
dotnet run --project "$here/FramePacing.Tests"

godot_dir="${1:-}"
[ -n "$godot_dir" ] || { echo "(skipping Godot runtime test: pass the Godot 4.7.2 .NET directory)"; exit 0; }
godot_bin="$(ls "$godot_dir"/Godot_v4.7.2-stable_mono_linux*.x86_64 2>/dev/null | head -1)"
[ -x "$godot_bin" ] || { echo "Godot binary not found in $godot_dir" >&2; exit 1; }

echo "== Godot runtime test (headless)"
rt="$here/godot-runtime"
# Godot.NET.Sdk + GodotSharp come from the packages bundled with the Godot release (offline).
cat > "$rt/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="godot-bundled" value="$godot_dir/GodotSharp/Tools/nupkgs" />
  </packageSources>
</configuration>
XML
export NUGET_PACKAGES="${NUGET_PACKAGES:-$rt/.nuget}"
dotnet build "$rt/FramePacingRuntimeTest.csproj" -nologo -v quiet
# --import once so Godot registers the C# script class, then run the test scene
"$godot_bin" --headless --path "$rt" --import >/dev/null 2>&1 || true
out=$("$godot_bin" --headless --path "$rt" 2>&1) || { echo "$out"; exit 1; }
echo "$out"
# the 25 s of simulated frames must have produced the logcat stats line read by tools/verify/device_check.py
grep -q '^\[svz-fps\] stats fps=[0-9.]* p95=[0-9.]*ms max=[0-9.]*ms missed=[0-9.]*% display=-\?[0-9]*Hz target=[0-9]*$' <<<"$out" \
  || { echo "FAIL: no [svz-fps] stats line in the Godot output"; exit 1; }
echo "stats line present: $(grep -m1 '\[svz-fps\] stats' <<<"$out")"
grep '\[svz-fps\] stats' <<<"$out" | python3 -I -c "import sys; sys.path.insert(0, sys.argv[1]); import device_check as d; \
  s, _ = d.parse_game_log(sys.stdin.read()); assert s and s[0]['target'] == 120, s; print('device_check parses it:', s[0])" "$here/../tools/verify"

echo "== Android refresh-rate bridge against stand-in Java objects"
"$godot_bin" --headless --path "$rt" -- --phase=bridge

echo "== settings survive an app restart (three separate processes)"
for phase in restart-1 restart-2 restart-3; do
  "$godot_bin" --headless --path "$rt" -- --phase=$phase
done
