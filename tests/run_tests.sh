#!/usr/bin/env bash
# Off-device tests for SvZ Enhanced. Needs only a .NET 8+ SDK (and, for the runtime test, the
# official Godot 4.7.2 .NET build). Works offline: no NuGet.org access is required.
#
#   tests/run_tests.sh                         # policy tests only
#   tests/run_tests.sh /path/to/Godot_v4.7.2-stable_mono_linux_x86_64   # + headless Godot runtime test
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

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
"$godot_bin" --headless --path "$rt"

echo "== Android refresh-rate bridge against stand-in Java objects"
"$godot_bin" --headless --path "$rt" -- --phase=bridge

echo "== settings survive an app restart (three separate processes)"
for phase in restart-1 restart-2 restart-3; do
  "$godot_bin" --headless --path "$rt" -- --phase=$phase
done
