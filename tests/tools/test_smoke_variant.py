"""tools/binpatch/smoke_variant.py: emulator-only APK variants with extra Godot command-line arguments."""
import os
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "binpatch"))
import smoke_variant as sv  # noqa: E402


class SmokeVariant(unittest.TestCase):
    def test_roundtrip_and_variant(self):
        base = ["--xr_mode_regular", "--fullscreen", "--background_color", "#000000"]
        self.assertEqual(sv.read_args(sv.write_args(base)), base)
        self.assertEqual(sv.read_args(b""), [])
        with tempfile.TemporaryDirectory() as tmp:
            src, dst = os.path.join(tmp, "in.apk"), os.path.join(tmp, "out.apk")
            with zipfile.ZipFile(src, "w") as z:
                z.writestr("assets/_cl_", sv.write_args(base))
                z.writestr(zipfile.ZipInfo("resources.arsc"), b"arsc")   # stored
                z.writestr("META-INF/CERT.RSA", b"old signature")
                z.writestr("classes.dex", b"dex", compress_type=zipfile.ZIP_DEFLATED)
            r = subprocess.run([sys.executable, "-I", os.path.join(ROOT, "tools", "binpatch", "smoke_variant.py"),
                                src, dst, "--", "--level=2", "--killall=30"], capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, r.stderr)
            with zipfile.ZipFile(dst) as z:
                self.assertEqual(sv.read_args(z.read("assets/_cl_")), base + ["--", "--level=2", "--killall=30"])
                self.assertNotIn("META-INF/CERT.RSA", z.namelist(), "old signature must be dropped")
                self.assertEqual(z.getinfo("resources.arsc").compress_type, zipfile.ZIP_STORED)
                self.assertEqual(z.read("classes.dex"), b"dex")
            with zipfile.ZipFile(src) as z:
                self.assertEqual(sv.read_args(z.read("assets/_cl_")), base, "input APK unchanged")

    def test_usage_errors(self):
        r = subprocess.run([sys.executable, "-I", os.path.join(ROOT, "tools", "binpatch", "smoke_variant.py"), "a.apk"],
                           capture_output=True, text=True)
        self.assertEqual(r.returncode, 2)


if __name__ == "__main__":
    unittest.main()
