"""Offline regression tests for the emulator launcher function (no Android SDK needed)."""
import pathlib
import re
import shlex
import subprocess
import tempfile
import unittest


SCRIPT = pathlib.Path(__file__).resolve().parents[2] / "tools/binpatch/emulator_smoke.sh"


class LauncherTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        source = SCRIPT.read_text()
        match = re.search(r"(?ms)^start_app\(\) \{.*?^\}", source)
        assert match, "smoke script must expose start_app()"
        cls.function = match.group()

    def run_stub(self, output, code=0):
        with tempfile.TemporaryDirectory() as out:
            bash = (
                "set -uo pipefail\n"
                'out="$1"; pkg=io.example.svz; fail=()\n'
                f'adb() {{ printf \'%s\\n\' {shlex.quote(output)}; return {code}; }}\n'
                + self.function + "\n"
                + "if start_app start; then status=pass; else status=fail; fi\n"
                + 'printf "%s:%s\\n" "$status" "${#fail[@]}"\n'
            )
            run = subprocess.run(["bash", "-c", bash, "test", out],
                                 text=True, capture_output=True, timeout=10)
            logfile = pathlib.Path(out) / "launch-start.txt"
            return run, logfile.read_text() if logfile.exists() else ""

    def test_success_under_nounset(self):
        run, log = self.run_stub("Events injected: 1")
        self.assertEqual(run.returncode, 0, run.stderr)
        self.assertIn("pass:0", run.stdout)
        self.assertIn("Events injected: 1", log)

    def test_missing_activity_fails_closed(self):
        run, log = self.run_stub("No activities found")
        self.assertEqual(run.returncode, 0, run.stderr)
        self.assertIn("fail:1", run.stdout)
        self.assertIn("No activities found", log)

    def test_adb_error_fails_closed(self):
        run, log = self.run_stub("device offline", 1)
        self.assertEqual(run.returncode, 0, run.stderr)
        self.assertIn("fail:1", run.stdout)
        self.assertIn("device offline", log)


if __name__ == "__main__":
    unittest.main()
