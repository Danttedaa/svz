#!/usr/bin/env python3
"""Helpers for the private SvZ Enhanced build (no third-party packages needed except where noted).

  config.py get KEY                      print a value from svz-build.json
  config.py set KEY VALUE                set a pin (original_sha256 / cert_sha256) in svz-build.json
  config.py validate                     check svz-build.json
  config.py fingerprint < keytool-output print the certificate SHA-256 from `keytool -list -v`
  config.py check-original APK [OBB]     SHA-256 of the original + sanity checks (is it SvZ Defense 3.4?)

The config file is svz-build.json next to the ci/ directory (override with SVZ_CONFIG).
"""
import hashlib
import json
import os
import re
import sys
import zipfile

CONFIG = os.environ.get("SVZ_CONFIG") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "svz-build.json")
DEFAULTS = {
    "engine_repo": "Danttedaa/svz",
    "engine_ref": "main",
    "package": "io.github.danttedaa.svz",
    "app_name": "SvZ Enhanced",
    "version_code_base": 1000,
    "keep_builds": 5,
    # release tags never deleted by the keep_builds cleanup (known-good rollback builds, e.g. v1003)
    "keep_tags": [],
    "original_sha256": "",
    "cert_sha256": "",
    # build from a prebuilt port APK (build-from-port.yml); its SHA-256 and signer are pinned here
    "port_apk_url": "",
    "port_apk_sha256": "",
    "port_signer_sha256": "",
}
PINS = {"original_sha256", "cert_sha256"}
ORIGINAL_PACKAGE = "com.glu.samuzombie"


class ConfigError(ValueError):
    pass


def load(path=CONFIG):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    cfg = dict(DEFAULTS)
    cfg.update(data)
    validate(cfg)
    return cfg


def validate(cfg):
    unknown = set(cfg) - set(DEFAULTS)
    if unknown:
        raise ConfigError(f"unknown keys: {', '.join(sorted(unknown))}")
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", cfg["engine_repo"]):
        raise ConfigError("engine_repo must be OWNER/NAME")
    if not re.fullmatch(r"[\w./-]+", cfg["engine_ref"]) or ".." in cfg["engine_ref"]:
        raise ConfigError("engine_ref is not a valid branch, tag or commit")
    if not re.fullmatch(r"[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+", cfg["package"]):
        raise ConfigError("package is not a valid Android package id")
    if not cfg["app_name"].strip() or '"' in cfg["app_name"]:
        raise ConfigError("app_name is invalid")
    if not isinstance(cfg["keep_tags"], list) or not all(isinstance(t, str) and re.fullmatch(r"apk-[\w.-]+", t) for t in cfg["keep_tags"]):
        raise ConfigError("keep_tags must be a list of apk-... release tags")
    for k in ("version_code_base", "keep_builds"):
        if not isinstance(cfg[k], int) or cfg[k] < 1:
            raise ConfigError(f"{k} must be a positive integer")
    if cfg["original_sha256"] and not re.fullmatch(r"[0-9a-f]{64}", cfg["original_sha256"]):
        raise ConfigError("original_sha256 must be 64 lower-case hex digits or empty")
    if cfg["cert_sha256"] and not re.fullmatch(r"[0-9A-F]{64}", cfg["cert_sha256"]):
        raise ConfigError("cert_sha256 must be 64 upper-case hex digits or empty")
    if cfg["port_apk_url"] and not re.fullmatch(r"https://github\.com/[\w.-]+/[\w.-]+/releases/download/[\w.%-]+/[\w.%-]+\.apk", cfg["port_apk_url"]):
        raise ConfigError("port_apk_url must be a GitHub release asset URL ending in .apk")
    if cfg["port_apk_url"] and not re.fullmatch(r"[0-9a-f]{64}", cfg["port_apk_sha256"]):
        raise ConfigError("port_apk_sha256 (64 lower-case hex digits) is required with port_apk_url")
    if cfg["port_signer_sha256"] and not re.fullmatch(r"[0-9A-F]{64}", cfg["port_signer_sha256"]):
        raise ConfigError("port_signer_sha256 must be 64 upper-case hex digits or empty")


def set_pin(key, value, path=CONFIG):
    """Pins are written once. Replacing one needs a manual edit (a different key breaks updates)."""
    if key not in PINS:
        raise ConfigError(f"only {', '.join(sorted(PINS))} can be set")
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    current = data.get(key, "")
    if current and current != value:
        raise ConfigError(f"{key} is already pinned to a different value; edit svz-build.json by hand to change it")
    data[key] = value
    cfg = dict(DEFAULTS)
    cfg.update(data)
    validate(cfg)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
        f.write("\n")


def keytool_fingerprint(text):
    for line in (text or "").splitlines():
        m = re.match(r"^\s*SHA-?256:\s*([0-9A-Fa-f:]+)\s*$", line)
        if m:
            fp = m.group(1).replace(":", "").upper()
            if re.fullmatch(r"[0-9A-F]{64}", fp):
                return fp
    return ""


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def check_original(apk, obb=None, require_manifest=False):
    """Return (sha256, notes). Raises ConfigError if this cannot be the SvZ Defense 3.4 APK."""
    try:
        with zipfile.ZipFile(apk) as z:
            names = set(z.namelist())
    except zipfile.BadZipFile:
        raise ConfigError(f"{os.path.basename(apk)} is not an APK (not a zip file)")
    if "AndroidManifest.xml" not in names or "classes.dex" not in names:
        raise ConfigError(f"{os.path.basename(apk)} is not an APK")
    has_data = any(n.startswith("assets/bin/Data/") for n in names)
    if not has_data and not obb:
        raise ConfigError("the APK has no assets/bin/Data: its game data is in an OBB; upload the main.*.obb too")
    notes = []
    try:  # optional: package and version from the manifest
        from loguru import logger
        logger.remove()
    except ImportError:
        pass
    try:
        from androguard.core.apk import APK
        a = APK(apk)
        pkg, ver = a.get_package(), a.get_androidversion_name() or ""
        if pkg != ORIGINAL_PACKAGE:
            raise ConfigError(f"package is {pkg}, expected {ORIGINAL_PACKAGE} (Samurai vs Zombies Defense)")
        if not ver.startswith("3.4"):
            raise ConfigError(f"version {ver}: the port supports Samurai vs Zombies Defense 3.4.x")
        notes.append(f"{pkg} {ver}")
    except ImportError:
        if require_manifest:
            raise ConfigError("androguard is needed to check the package and version (pip install androguard==4.1.4)")
        notes.append("androguard not installed: package/version not checked")
    return sha256_file(apk), notes


def main(argv):
    try:
        if not argv:
            raise ConfigError(__doc__)
        cmd = argv[0]
        if cmd == "get":
            v = load()[argv[1]]
            print("\n".join(v) if isinstance(v, list) else v)
        elif cmd == "set":
            set_pin(argv[1], argv[2])
        elif cmd == "validate":
            load()
            print("svz-build.json OK")
        elif cmd == "fingerprint":
            fp = keytool_fingerprint(sys.stdin.read())
            if not fp:
                return 1
            print(fp)
        elif cmd == "check-original":
            sha, notes = check_original(argv[1], argv[2] if len(argv) > 2 else None, require_manifest=True)
            print(sha)
            for n in notes:
                print("note: " + n, file=sys.stderr)
        else:
            raise ConfigError(f"unknown command {cmd}")
    except (ConfigError, OSError, KeyError, IndexError, json.JSONDecodeError) as e:
        print(f"config: {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
