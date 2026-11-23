#!/usr/bin/env python3
"""Set package id, app name and version of the Android export preset in godot/export_presets.cfg.

Only the options section of the preset whose platform is "Android" is changed; every other line is kept
byte for byte. Values are validated, because a bad package id or a lower version code makes Android refuse
the install (and a different package id means a separate app with its own, empty save data).

usage: set_android_preset.py PRESETS [--package ID] [--app-name NAME] [--version-code N] [--version-name S]
"""
import argparse
import re
import sys

PACKAGE_RE = re.compile(r"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$")
MAX_VERSION_CODE = 2100000000  # Google Play / Android limit


class PresetError(ValueError):
    pass


def validate(package=None, app_name=None, version_code=None, version_name=None):
    if package is not None and not PACKAGE_RE.match(package):
        raise PresetError(f"invalid Android package id: {package!r}")
    if app_name is not None and (not app_name.strip() or '"' in app_name or "\n" in app_name):
        raise PresetError(f"invalid app name: {app_name!r}")
    if version_code is not None:
        if not str(version_code).isdigit() or not 1 <= int(version_code) <= MAX_VERSION_CODE:
            raise PresetError(f"version code must be 1..{MAX_VERSION_CODE}: {version_code!r}")
    if version_name is not None and (not version_name.strip() or '"' in version_name or "\n" in version_name):
        raise PresetError(f"invalid version name: {version_name!r}")


def android_options_section(lines):
    """Return the [preset.N.options] header name for the preset with platform="Android"."""
    current = None
    for line in lines:
        m = re.match(r"^\[preset\.(\d+)\]\s*$", line)
        if m:
            current = m.group(1)
        elif current is not None and line.strip() == 'platform="Android"':
            return f"[preset.{current}.options]"
    raise PresetError("no Android preset found")


def apply(text, package=None, app_name=None, version_code=None, version_name=None):
    validate(package, app_name, version_code, version_name)
    wanted = {}
    if package is not None:
        wanted["package/unique_name"] = f'"{package}"'
    if app_name is not None:
        wanted["package/name"] = f'"{app_name}"'
    if version_code is not None:
        wanted["version/code"] = str(int(version_code))
    if version_name is not None:
        wanted["version/name"] = f'"{version_name}"'
    lines = text.split("\n")
    header = android_options_section(lines)
    out, in_section, seen = [], False, set()
    for line in lines:
        if line.startswith("["):
            if in_section:  # leaving the section: add keys that were not present
                out.extend(f"{k}={v}" for k, v in wanted.items() if k not in seen)
                seen.update(wanted)
            in_section = line.strip() == header
        elif in_section and "=" in line:
            key = line.split("=", 1)[0]
            if key in wanted:
                line = f"{key}={wanted[key]}"
                seen.add(key)
        out.append(line)
    if in_section:
        out.extend(f"{k}={v}" for k, v in wanted.items() if k not in seen)
    return "\n".join(out)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("presets")
    p.add_argument("--package")
    p.add_argument("--app-name")
    p.add_argument("--version-code")
    p.add_argument("--version-name")
    a = p.parse_args(argv)
    try:
        with open(a.presets, encoding="utf-8") as f:
            text = f.read()
        new = apply(text, a.package, a.app_name, a.version_code, a.version_name)
    except (OSError, PresetError) as e:
        print(f"set_android_preset: {e}", file=sys.stderr)
        return 2
    with open(a.presets, "w", encoding="utf-8") as f:
        f.write(new)
    return 0


if __name__ == "__main__":
    sys.exit(main())
