#!/usr/bin/env python3
"""Check a built SvZ Enhanced APK before it is published (read-only; installs nothing).

Checks (each can be required on the command line):
  - native code for arm64-v8a (the Galaxy S25 Ultra has no 32-bit support), including libgodot_android.so
  - package id, minimum versionCode
  - no forbidden permissions (default: android.permission.INTERNET: the game must stay offline)
  - signing certificate SHA-256 (same key = updates keep the save game)
  - the Java methods the 120 Hz refresh-rate bridge calls are present and public in the APK's DEX
  - optional: `apksigner verify` (Android build-tools) accepts the signature

Needs androguard (pip install androguard==4.1.4). Prints a report; --json writes the facts and results.
Exit code: 0 all required checks passed, 1 a check failed, 2 usage or read error.

usage: verify_apk.py APK [--expect-package ID] [--min-version-code N] [--require-abi arm64-v8a]
                     [--forbid-permission P ...] [--expect-cert-sha256 HEX] [--check-bridge]
                     [--apksigner PATH] [--json OUT]
"""
import argparse
import hashlib
import json
import re
import subprocess
import sys
import zipfile

# The refresh-rate bridge in godot/src/Engine/Core/FramePacing.cs (SendFrameRate) calls these.
BRIDGE_METHODS = [
    ("Lorg/godotengine/godot/GodotActivity;", "getGodot"),
    ("Lorg/godotengine/godot/Godot;", "getRenderView"),
    ("Lorg/godotengine/godot/GodotRenderView;", "getView"),
    ("Lorg/godotengine/godot/plugin/AndroidRuntimePlugin;", "getActivity"),
    ("Lorg/godotengine/godot/plugin/AndroidRuntimePlugin;", "createRunnableFromGodotCallable"),
]


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def zip_facts(path):
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
    libs = {}
    for n in names:
        parts = n.split("/")
        if len(parts) == 3 and parts[0] == "lib" and n.endswith(".so"):
            libs.setdefault(parts[1], []).append(parts[2])
    return {"abis": sorted(libs), "native_libs": {k: sorted(v) for k, v in libs.items()}}


def androguard_facts(path, check_bridge):
    try:
        from loguru import logger
        logger.remove()
    except ImportError:
        pass
    from androguard.core.apk import APK
    a = APK(path)
    facts = {
        "package": a.get_package(),
        "version_code": int(a.get_androidversion_code() or 0),
        "version_name": a.get_androidversion_name(),
        "min_sdk": int(a.get_min_sdk_version() or 0),
        "target_sdk": int(a.get_target_sdk_version() or 0),
        "permissions": sorted(a.get_permissions()),
        "main_activity": a.get_main_activity(),
        "cert_sha256": sorted({hashlib.sha256(c).hexdigest().upper() for c in
                               list(a.get_certificates_der_v3()) + list(a.get_certificates_der_v2()) +
                               [x.dump() for x in a.get_certificates_v1()]}),
    }
    if check_bridge:
        from androguard.core.dex import DEX
        found = {}
        for dexbytes in a.get_all_dex():
            for c in DEX(dexbytes).get_classes():
                for m in c.get_methods():
                    key = (c.get_name(), m.get_name())
                    if key in BRIDGE_METHODS:
                        found[key] = "public" in m.get_access_flags_string().split()
        facts["bridge_methods"] = {f"{c}->{m}": found.get((c, m)) for c, m in BRIDGE_METHODS}
    return facts


def apksigner_facts(apksigner, path):
    r = subprocess.run([apksigner, "verify", "--print-certs", path], capture_output=True, text=True)
    certs = sorted({m.group(1).upper() for m in re.finditer(r"certificate SHA-256 digest:\s*([0-9a-fA-F]{64})", r.stdout)})
    return {"apksigner_ok": r.returncode == 0, "apksigner_cert_sha256": certs,
            "apksigner_error": (r.stderr or r.stdout).strip()[-500:] if r.returncode else ""}


def evaluate(facts, expect):
    """Pure decision logic: list of (name, passed, detail)."""
    res = []
    for abi in expect.get("require_abis", []):
        libs = facts.get("native_libs", {}).get(abi, [])
        res.append((f"abi {abi}", bool(libs) and "libgodot_android.so" in libs, ", ".join(libs) or "missing"))
    if expect.get("package"):
        res.append(("package", facts.get("package") == expect["package"], str(facts.get("package"))))
    if expect.get("min_version_code"):
        vc = facts.get("version_code", 0)
        res.append(("versionCode", vc >= expect["min_version_code"], f"{vc} (min {expect['min_version_code']})"))
    for perm in expect.get("forbid_permissions", []):
        res.append((f"no {perm}", perm not in facts.get("permissions", []), "present" if perm in facts.get("permissions", []) else "absent"))
    if expect.get("cert_sha256"):
        want = re.sub(r"[\s:]", "", expect["cert_sha256"]).upper()
        certs = facts.get("cert_sha256", [])
        res.append(("signing certificate", certs == [want], ", ".join(certs) or "unsigned"))
    if expect.get("check_bridge"):
        bm = facts.get("bridge_methods", {})
        bad = [k for k, v in bm.items() if v is not True]
        res.append(("refresh-rate bridge methods", bool(bm) and not bad, "all public" if bm and not bad else "missing/non-public: " + ", ".join(bad)))
    if "apksigner_ok" in facts:
        res.append(("apksigner verify", facts["apksigner_ok"], facts.get("apksigner_error") or "ok"))
        if expect.get("cert_sha256"):
            want = re.sub(r"[\s:]", "", expect["cert_sha256"]).upper()
            res.append(("apksigner certificate", facts.get("apksigner_cert_sha256") == [want], ", ".join(facts.get("apksigner_cert_sha256", []))))
    return res


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("apk")
    p.add_argument("--expect-package")
    p.add_argument("--min-version-code", type=int)
    p.add_argument("--require-abi", action="append", default=[])
    p.add_argument("--forbid-permission", action="append")
    p.add_argument("--expect-cert-sha256")
    p.add_argument("--check-bridge", action="store_true")
    p.add_argument("--apksigner")
    p.add_argument("--json")
    a = p.parse_args(argv)
    forbid = a.forbid_permission if a.forbid_permission is not None else ["android.permission.INTERNET"]
    try:
        facts = {"file": a.apk, "sha256": sha256_file(a.apk)}
        facts.update(zip_facts(a.apk))
        facts.update(androguard_facts(a.apk, a.check_bridge))
        if a.apksigner:
            facts.update(apksigner_facts(a.apksigner, a.apk))
    except ImportError:
        print("verify_apk: androguard is required (pip install androguard==4.1.4)", file=sys.stderr)
        return 2
    except (OSError, zipfile.BadZipFile) as e:
        print(f"verify_apk: cannot read {a.apk}: {e}", file=sys.stderr)
        return 2
    expect = {"require_abis": a.require_abi, "package": a.expect_package, "min_version_code": a.min_version_code,
              "forbid_permissions": forbid, "cert_sha256": a.expect_cert_sha256, "check_bridge": a.check_bridge}
    results = evaluate(facts, expect)
    print(f"{a.apk}\n  sha256 {facts['sha256']}\n  package {facts.get('package')} versionCode {facts.get('version_code')} "
          f"targetSdk {facts.get('target_sdk')}\n  ABIs {', '.join(facts['abis']) or '(none)'}")
    for name, ok, detail in results:
        print(f"  [{'PASS' if ok else 'FAIL'}] {name}: {detail}")
    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            json.dump({"facts": facts, "results": [{"check": n, "passed": ok, "detail": d} for n, ok, d in results]}, f, indent=2)
    return 0 if all(ok for _, ok, _ in results) else 1


if __name__ == "__main__":
    sys.exit(main())
