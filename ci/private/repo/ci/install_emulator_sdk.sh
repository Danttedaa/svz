#!/usr/bin/env bash
# Retry transient Android SDK image download failures on hosted CI.
# Does not change any device, game assets, signatures or user configuration.
set -euo pipefail
sdkmanager="$ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager"
if [ ! -x "$sdkmanager" ]; then
  echo "Emulator preinstall skipped: sdkmanager not present at $sdkmanager"
  exit 0
fi
image="system-images;android-34;google_apis;x86_64"
for attempt in 1 2 3; do
  echo "Android 34 SDK image install attempt $attempt/3"
  if "$sdkmanager" --install "$image" "platforms;android-34" "emulator"; then
    echo "Android 34 emulator prerequisites ready"
    exit 0
  fi
  if [ "$attempt" -lt 3 ]; then sleep 8; fi
done
echo "Preinstall failed; emulator action will try its own setup" >&2
exit 1
