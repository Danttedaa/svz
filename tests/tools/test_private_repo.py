"""Tests for the private build repository template (ci/private/repo): config pins, signing key handling,
checks on the original game file. No network, no game files."""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
REPO = os.path.join(ROOT, "ci", "private", "repo")
sys.path.insert(0, os.path.join(REPO, "ci"))
import config as cfgmod  # noqa: E402

PASS = "test-passphrase-0123456789-abcdef"


class Config(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.path = os.path.join(self.tmp, "svz-build.json")
        shutil.copy(os.path.join(REPO, "svz-build.json"), self.path)

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def test_template_is_valid(self):
        c = cfgmod.load(self.path)
        self.assertEqual(c["engine_repo"], "Danttedaa/svz")
        self.assertEqual(c["original_sha256"], "")
        self.assertEqual(c["cert_sha256"], "")

    def test_pins_are_write_once(self):
        cfgmod.set_pin("cert_sha256", "A" * 64, self.path)
        cfgmod.set_pin("cert_sha256", "A" * 64, self.path)  # same value: fine
        with self.assertRaises(cfgmod.ConfigError):
            cfgmod.set_pin("cert_sha256", "B" * 64, self.path)
        with self.assertRaises(cfgmod.ConfigError):
            cfgmod.set_pin("package", "com.evil.x", self.path)
        self.assertEqual(cfgmod.load(self.path)["cert_sha256"], "A" * 64)

    def test_invalid_values(self):
        bad = [("engine_repo", "not a repo"), ("engine_ref", "a..b"), ("package", "svz"),
               ("keep_builds", 0), ("original_sha256", "XYZ"), ("cert_sha256", "a" * 64), ("surprise", 1)]
        for key, value in bad:
            with open(self.path) as f:
                data = json.load(f)
            data[key] = value
            with open(self.path, "w") as f:
                json.dump(data, f)
            with self.assertRaises(cfgmod.ConfigError, msg=key):
                cfgmod.load(self.path)
            shutil.copy(os.path.join(REPO, "svz-build.json"), self.path)

    def test_keytool_fingerprint(self):
        out = "Alias name: svz\n\t SHA1: 11:22\n\t SHA256: " + ":".join(["AB"] * 32) + "\n"
        self.assertEqual(cfgmod.keytool_fingerprint(out), "AB" * 32)
        self.assertEqual(cfgmod.keytool_fingerprint("nothing"), "")


class Original(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def make_zip(self, name, files):
        p = os.path.join(self.tmp, name)
        with zipfile.ZipFile(p, "w") as z:
            for f in files:
                z.writestr(f, b"x")
        return p

    def test_not_an_apk(self):
        p = os.path.join(self.tmp, "x.apk")
        with open(p, "wb") as f:
            f.write(b"hello")
        with self.assertRaisesRegex(cfgmod.ConfigError, "not an APK"):
            cfgmod.check_original(p)
        with self.assertRaisesRegex(cfgmod.ConfigError, "not an APK"):
            cfgmod.check_original(self.make_zip("y.apk", ["readme.txt"]))

    def test_needs_game_data_or_obb(self):
        apk = self.make_zip("svz.apk", ["AndroidManifest.xml", "classes.dex"])
        with self.assertRaisesRegex(cfgmod.ConfigError, "OBB"):
            cfgmod.check_original(apk)

    def test_wrong_game_rejected(self):
        """A real APK of another game must be rejected (uses local files when present)."""
        svz2 = "/home/claude/rel/svz2.apk"
        if not os.path.exists(svz2):
            self.skipTest("SvZ2 sample APK not available")
        try:
            import androguard  # noqa: F401
        except ImportError:
            self.skipTest("androguard not installed")
        with self.assertRaisesRegex(cfgmod.ConfigError, "no assets/bin/Data|expected com.glu.samuzombie"):
            cfgmod.check_original(svz2, obb="dummy.obb")

    def test_cli_reports_errors(self):
        r = subprocess.run([sys.executable, "-I", os.path.join(REPO, "ci", "config.py"), "check-original", "/nonexistent.apk"],
                           capture_output=True, text=True, stdin=subprocess.DEVNULL)
        self.assertEqual(r.returncode, 2)


@unittest.skipUnless(shutil.which("keytool") and shutil.which("openssl"), "needs keytool and openssl")
class Signing(unittest.TestCase):
    SCRIPT = os.path.join(REPO, "ci", "signing.sh")

    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.enc = os.path.join(self.tmp, "signing", "release-key.p12.enc")

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def run_sig(self, *args, passphrase=PASS):
        env = dict(os.environ, SVZ_KEY_PASSPHRASE=passphrase)
        return subprocess.run(["bash", self.SCRIPT, *args], capture_output=True, text=True, env=env,
                              stdin=subprocess.DEVNULL, timeout=120)

    def test_round_trip_and_guards(self):
        r = self.run_sig("generate", self.enc)
        self.assertEqual(r.returncode, 0, r.stderr)
        fp = r.stdout.strip()
        self.assertRegex(fp, r"^[0-9A-F]{64}$")
        with open(self.enc, "rb") as f:
            self.assertEqual(f.read(8), b"Salted__")  # OpenSSL envelope, not a bare keystore

        r = self.run_sig("generate", self.enc)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("refusing to replace", r.stderr)

        out = os.path.join(self.tmp, "k.p12")
        r = self.run_sig("decrypt", self.enc, out)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(r.stdout.strip(), fp)
        self.assertEqual(os.stat(out).st_mode & 0o777, 0o600)

        out2 = os.path.join(self.tmp, "k2.p12")
        r = self.run_sig("decrypt", self.enc, out2, passphrase="a-wrong-passphrase-of-enough-length")
        self.assertNotEqual(r.returncode, 0)
        self.assertFalse(os.path.exists(out2))

        # the passphrase never appears in the encrypted file
        with open(self.enc, "rb") as f:
            self.assertNotIn(PASS.encode(), f.read())

    def test_short_or_missing_passphrase(self):
        self.assertNotEqual(self.run_sig("generate", self.enc, passphrase="short").returncode, 0)
        self.assertNotEqual(self.run_sig("generate", self.enc, passphrase="").returncode, 0)
        self.assertFalse(os.path.exists(self.enc))


if __name__ == "__main__":
    unittest.main()
