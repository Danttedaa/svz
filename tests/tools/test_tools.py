"""Unit tests for the build/verification tools (no device, no game files, no third-party packages).

python3 -m unittest discover -s tests/tools
"""
import os
import subprocess
import sys
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools"))
sys.path.insert(0, os.path.join(ROOT, "tools", "verify"))

import set_android_preset as sap  # noqa: E402
import cert_fingerprint as cf  # noqa: E402
import verify_apk as va  # noqa: E402
import device_check as dc  # noqa: E402

with open(os.path.join(ROOT, "godot", "export_presets.cfg"), encoding="utf-8") as _f:
    PRESETS = _f.read()


class SetAndroidPreset(unittest.TestCase):
    def test_changes_only_android_options(self):
        out = sap.apply(PRESETS, package="io.github.danttedaa.svz", app_name="SvZ Enhanced",
                        version_code="1042", version_name="m1-abc123")
        self.assertIn('package/unique_name="io.github.danttedaa.svz"', out)
        self.assertIn('package/name="SvZ Enhanced"', out)
        self.assertIn("version/code=1042", out)
        self.assertIn('version/name="m1-abc123"', out)
        # the macOS preset's own bundle id and every other line stay untouched
        self.assertIn('application/bundle_identifier="com.mesflit.svz"', out)
        changed = [(a, b) for a, b in zip(PRESETS.split("\n"), out.split("\n")) if a != b]
        self.assertEqual(len(changed), 4)

    def test_no_arguments_is_identity(self):
        self.assertEqual(sap.apply(PRESETS), PRESETS)

    def test_validation(self):
        for bad in ["svz", "1com.x", "com..x", "com.x-y"]:
            with self.assertRaises(sap.PresetError):
                sap.apply(PRESETS, package=bad)
        for bad in ["0", "-1", "abc", "2100000001"]:
            with self.assertRaises(sap.PresetError):
                sap.apply(PRESETS, version_code=bad)
        with self.assertRaises(sap.PresetError):
            sap.apply(PRESETS, app_name='a"b')

    def test_cli_round_trip(self, ):
        import tempfile
        with tempfile.NamedTemporaryFile("w", suffix=".cfg", delete=False, encoding="utf-8") as f:
            f.write(PRESETS)
        try:
            r = subprocess.run([sys.executable, "-I", os.path.join(ROOT, "tools", "set_android_preset.py"), f.name,
                                "--version-code", "7"], capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, r.stderr)
            with open(f.name, encoding="utf-8") as g:
                self.assertIn("version/code=7", g.read())
            r = subprocess.run([sys.executable, "-I", os.path.join(ROOT, "tools", "set_android_preset.py"), f.name,
                                "--package", "bad"], capture_output=True, text=True)
            self.assertEqual(r.returncode, 2)
        finally:
            os.unlink(f.name)


class CertFingerprint(unittest.TestCase):
    KEYTOOL = """Alias name: svz
Certificate fingerprints:
	 SHA1: 11:22:33:44:55:66:77:88:99:00:AA:BB:CC:DD:EE:FF:11:22:33:44
	 SHA256: 9A:A3:42:5B:8B:9D:C1:DA:4B:7F:0E:98:16:31:1E:3C:08:56:9D:2B:E9:52:BC:A8:C4:D2:21:69:29:69:82:4F
Signature algorithm name: SHA384withRSA
"""

    def test_keytool(self):
        self.assertEqual(cf.from_keytool(self.KEYTOOL), "9AA3425B8B9DC1DA4B7F0E9816311E3C08569D2BE952BCA8C4D221692969824F")

    def test_normalize(self):
        self.assertEqual(cf.normalize("9a:a3:42:5b:8b:9d:c1:da:4b:7f:0e:98:16:31:1e:3c:08:56:9d:2b:e9:52:bc:a8:c4:d2:21:69:29:69:82:4f\n"),
                         "9AA3425B8B9DC1DA4B7F0E9816311E3C08569D2BE952BCA8C4D221692969824F")
        self.assertEqual(cf.normalize("not a fingerprint"), "")
        self.assertEqual(cf.from_keytool("no fingerprint here"), "")


GOOD = {
    "native_libs": {"arm64-v8a": ["libgodot_android.so", "libmonosgen-2.0.so"]},
    "package": "io.github.danttedaa.svz", "version_code": 1042, "permissions": [],
    "cert_sha256": ["9AA3425B8B9DC1DA4B7F0E9816311E3C08569D2BE952BCA8C4D221692969824F"],
    "bridge_methods": {"a": True, "b": True},
}
EXPECT = {"require_abis": ["arm64-v8a"], "package": "io.github.danttedaa.svz", "min_version_code": 1000,
          "forbid_permissions": ["android.permission.INTERNET"],
          "cert_sha256": "9a:a3:42:5b:8b:9d:c1:da:4b:7f:0e:98:16:31:1e:3c:08:56:9d:2b:e9:52:bc:a8:c4:d2:21:69:29:69:82:4f",
          "check_bridge": True}


class VerifyApk(unittest.TestCase):
    def failed(self, facts):
        return [n for n, ok, _ in va.evaluate(facts, EXPECT) if not ok]

    def test_good_apk_passes(self):
        self.assertEqual(self.failed(GOOD), [])

    def test_each_problem_fails(self):
        cases = {
            "abi arm64-v8a": {"native_libs": {"armeabi-v7a": ["libgodot_android.so"]}},
            "package": {"package": "com.mesflit.svz"},
            "versionCode": {"version_code": 999},
            "no android.permission.INTERNET": {"permissions": ["android.permission.INTERNET"]},
            "signing certificate": {"cert_sha256": ["00" * 32]},
            "refresh-rate bridge methods": {"bridge_methods": {"a": True, "b": None}},
        }
        for check, change in cases.items():
            facts = dict(GOOD, **change)
            self.assertEqual(self.failed(facts), [check], check)

    def test_two_signers_rejected(self):
        self.assertEqual(self.failed(dict(GOOD, cert_sha256=GOOD["cert_sha256"] + ["00" * 32])), ["signing certificate"])

    def test_apksigner_result_used(self):
        facts = dict(GOOD, apksigner_ok=False, apksigner_error="DOES NOT VERIFY", apksigner_cert_sha256=[])
        self.assertEqual(self.failed(facts), ["apksigner verify", "apksigner certificate"])


LATENCY_120 = "8333333\n" + "\n".join(
    f"{1000 + i * 8333333} {2000 + i * 8333333} {3000 + i * 8333333}" for i in range(127)) + \
    "\n0 9223372036854775807 0\n"


class DeviceCheck(unittest.TestCase):
    def test_parse_latency(self):
        period, presents = dc.parse_latency(LATENCY_120)
        self.assertEqual(period, 8333333)
        self.assertEqual(len(presents), 127)  # the pending frame is skipped

    def test_find_layer(self):
        listing = """com.android.systemui.ImageWallpaper#12
SurfaceView[io.github.danttedaa.svz/com.godot.game.GodotApp]#301
Background for SurfaceView[io.github.danttedaa.svz/com.godot.game.GodotApp]#302
SurfaceView[io.github.danttedaa.svz/com.godot.game.GodotApp](BLAST)#303
io.github.danttedaa.svz/com.godot.game.GodotApp#300"""
        self.assertEqual(dc.find_layer(listing, "io.github.danttedaa.svz"),
                         "SurfaceView[io.github.danttedaa.svz/com.godot.game.GodotApp](BLAST)#303")
        self.assertIsNone(dc.find_layer(listing, "com.other"))

    def test_120_passes(self):
        period, presents = dc.parse_latency(LATENCY_120)
        sf = dc.summarize_presents(presents, period, 120)
        self.assertAlmostEqual(sf["fps"], 120.0, delta=0.1)
        self.assertEqual(sf["display_hz"], 120.0)
        self.assertEqual(sf["missed_pct"], 0)
        stats, _ = dc.parse_game_log("11-23 godot: [svz-fps] stats fps=119.9 p95=8.4ms max=9.0ms missed=0.0% display=120Hz target=120")
        self.assertEqual(dc.verdict(sf, stats, 120)[0], "PASS")

    def test_60_hz_panel_fails(self):
        presents = [i * 16666667 for i in range(200)]
        sf = dc.summarize_presents(presents, 16666667, 120)
        result, reasons = dc.verdict(sf, [], 120)
        self.assertEqual(result, "FAIL")
        self.assertTrue(any("panel at 60.0 Hz" in r for r in reasons))

    def test_judder_fails(self):
        # 120 Hz panel, but every 4th frame misses a refresh
        t, presents = 0, []
        for i in range(400):
            t += 16666666 if i % 4 == 0 else 8333333
            presents.append(t)
        sf = dc.summarize_presents(presents, 8333333, 120)
        result, reasons = dc.verdict(sf, [], 120)
        self.assertEqual(result, "FAIL")
        self.assertTrue(any("missed" in r or "presented" in r for r in reasons))

    def test_game_log(self):
        log = """11-23 14:00:01.000 I godot: [svz-fps] Surface.setFrameRate(120) sent via Godot.getRenderView (API 37, seamless only)
11-23 14:00:02.000 I godot: [svz-fps] display after request: 120Hz -> maxFps=0 requestHz=120 vsyncPaced=True
11-23 14:00:07.000 I godot: [svz-fps] stats fps=118.8 p95=8.3ms max=16.7ms missed=1.0% display=120Hz target=120
11-23 14:00:07.000 I godot: unrelated"""
        stats, events = dc.parse_game_log(log)
        self.assertEqual(len(stats), 1)
        self.assertEqual(stats[0]["fps"], 118.8)
        self.assertEqual(stats[0]["display"], 120)
        self.assertEqual(len(events), 2)
        self.assertTrue(events[0].startswith("[svz-fps] Surface.setFrameRate(120) sent"))

    def test_no_data_cannot_pass(self):
        self.assertEqual(dc.verdict(None, [], 120)[0], "FAIL")
        self.assertIsNone(dc.summarize_presents([1, 2, 3], 8333333, 120))


if __name__ == "__main__":
    unittest.main()
