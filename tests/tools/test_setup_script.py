"""tools/private_build_setup.sh against a stub `gh` (records calls; no network, no real repository)."""
import json
import os
import shutil
import stat
import subprocess
import tempfile
import unittest
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SCRIPT = os.path.join(ROOT, "tools", "private_build_setup.sh")

FAKE_GH = r"""#!/usr/bin/env bash
# records argv and stdin of every call as one JSON line
stdin=""
if [ ! -t 0 ]; then stdin=$(cat); fi
python3 -I -c 'import json,sys; print(json.dumps({"argv": sys.argv[2:], "stdin": sys.argv[1]}))' "$stdin" "$@" >> "$GH_LOG"
case "$1 $2" in
  "repo view") echo "${FAKE_PRIVATE:-true}";;
  "release view") exit 1;;
  "api repos/"*) [ "$2" = "-X" ] && echo '{}' || exit 1;;
esac
exit 0
"""


@unittest.skipUnless(shutil.which("keytool") and shutil.which("unzip"), "needs keytool and unzip")
class PrivateBuildSetup(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        bin_dir = os.path.join(self.tmp, "bin")
        os.mkdir(bin_dir)
        gh = os.path.join(bin_dir, "gh")
        with open(gh, "w") as f:
            f.write(FAKE_GH)
        os.chmod(gh, 0o755)
        self.log = os.path.join(self.tmp, "gh.log")
        self.env = dict(os.environ, PATH=bin_dir + os.pathsep + os.environ["PATH"], HOME=os.path.join(self.tmp, "home"),
                        GH_LOG=self.log)
        os.mkdir(self.env["HOME"])
        self.apk = os.path.join(self.tmp, "SvZ.apk")
        with zipfile.ZipFile(self.apk, "w") as z:
            z.writestr("AndroidManifest.xml", b"x")
            z.writestr("assets/bin/Data/mainData", b"unity data")

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def run_setup(self, *extra, **env):
        e = dict(self.env, **env)
        return subprocess.run(["bash", SCRIPT, "--repo", "Danttedaa/svz-private", "--apk", self.apk, *extra],
                              capture_output=True, text=True, env=e, cwd=ROOT, timeout=300,
                              stdin=subprocess.DEVNULL)

    @staticmethod
    def read(path):
        with open(path) as f:
            return f.read()

    def calls(self):
        if not os.path.exists(self.log):
            return []
        with open(self.log) as f:
            return [json.loads(l) for l in f]

    def test_full_setup(self):
        r = self.run_setup("--engine-ref", "enhanced/m1-frame-pacing")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        calls = self.calls()
        argvs = [" ".join(c["argv"]) for c in calls]

        ks_dir = os.path.join(self.env["HOME"], ".svz-signing")
        password = self.read(os.path.join(ks_dir, "svz-release.password")).strip()
        self.assertEqual(stat.S_IMODE(os.stat(ks_dir).st_mode), 0o700)
        self.assertEqual(stat.S_IMODE(os.stat(os.path.join(ks_dir, "svz-release.jks")).st_mode), 0o600)

        secrets = {c["argv"][2]: c["stdin"] for c in calls if c["argv"][:2] == ["secret", "set"]}
        self.assertEqual(set(secrets), {"SVZ_KEYSTORE_B64", "SVZ_KEYSTORE_PASSWORD", "SVZ_KEYSTORE_ALIAS"})
        self.assertEqual(secrets["SVZ_KEYSTORE_PASSWORD"], password)
        self.assertEqual(secrets["SVZ_KEYSTORE_ALIAS"], "svz")
        # the password is never on a command line
        self.assertFalse(any(password in a for a in argvs))

        import hashlib
        with open(self.apk, "rb") as f:
            apk_sha = hashlib.sha256(f.read()).hexdigest()
        variables = {c["argv"][2]: c["argv"][c["argv"].index("--body") + 1] for c in calls if c["argv"][:2] == ["variable", "set"]}
        self.assertEqual(variables["SVZ_ORIGINAL_SHA256"], apk_sha)
        self.assertRegex(variables["SVZ_CERT_SHA256"], r"^[0-9A-F]{64}$")
        self.assertEqual(variables["SVZ_ENGINE_REF"], "enhanced/m1-frame-pacing")
        self.assertEqual(variables["SVZ_PACKAGE"], "io.github.danttedaa.svz")

        create = [c["argv"] for c in calls if c["argv"][:2] == ["release", "create"]]
        self.assertEqual(len(create), 1)
        self.assertEqual(os.path.basename(create[0][-1]), "svz-original.apk")
        self.assertTrue(any(a.startswith("api -X PUT repos/Danttedaa/svz-private/contents/.github/workflows/build-apk.yml") for a in argvs))
        self.assertTrue(any(a.startswith("workflow run build-apk.yml") for a in argvs))

        # second run: same key and same original are reused
        os.remove(self.log)
        r = self.run_setup("--no-run")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(self.read(os.path.join(ks_dir, "svz-release.password")).strip(), password)
        self.assertIn("using the existing key", r.stdout)

    def test_refuses_public_repository(self):
        r = self.run_setup(FAKE_PRIVATE="false")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("PUBLIC", r.stderr)
        self.assertFalse(any(c["argv"][:2] in (["secret", "set"], ["release", "create"]) for c in self.calls()))
        self.assertFalse(os.path.exists(os.path.join(self.env["HOME"], ".svz-signing")))

    def test_needs_game_data(self):
        with zipfile.ZipFile(self.apk, "w") as z:
            z.writestr("AndroidManifest.xml", b"x")
        r = self.run_setup()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("--obb", r.stderr)


if __name__ == "__main__":
    unittest.main()
