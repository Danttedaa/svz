"""Guards for the repository's publishing rules (run in CI with the other tool tests).

The game's code and assets are proprietary: anything built from them may only exist in a private repository.
These tests fail if a workflow in this public repository could publish a release, if the build workflow
loses its private-repository guard, if artifacts are kept for long, or if game files or signing keys are
committed.
"""
import glob
import os
import re
import subprocess
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PUBLIC_WORKFLOWS = sorted(glob.glob(os.path.join(ROOT, ".github", "workflows", "*.yml")))
PRIVATE_WORKFLOW = os.path.join(ROOT, "ci", "private", "build-apk.yml")
PRIVATE_GUARD = "github.event.repository.private != true"

try:
    import yaml
except ImportError:  # PyYAML is optional; the text checks still run
    yaml = None


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def steps(doc):
    for job in (doc.get("jobs") or {}).values():
        for st in job.get("steps") or []:
            yield st


class PublicWorkflows(unittest.TestCase):
    def test_found(self):
        self.assertTrue(PUBLIC_WORKFLOWS)

    def test_no_release_publishing(self):
        pattern = re.compile(r"gh\s+release\s+(create|upload|edit)|action-gh-release|create-release"
                             r"|api\.github\.com/repos/\S+/releases", re.I)
        for wf in PUBLIC_WORKFLOWS:
            self.assertIsNone(pattern.search(read(wf)), f"{os.path.basename(wf)} can publish a release")

    def test_no_write_permission_to_contents(self):
        for wf in PUBLIC_WORKFLOWS:
            self.assertNotRegex(read(wf), r"contents:\s*write", os.path.basename(wf))

    def test_builds_only_in_private_repositories(self):
        """Any workflow that touches the original game must stop first when the repository is public."""
        for wf in PUBLIC_WORKFLOWS:
            text = read(wf)
            if "SVZ_APK_URL" not in text and "tools/build.sh" not in text:
                continue
            first = text.index("steps:")
            guard = text.find(PRIVATE_GUARD, first)
            build = min(i for i in (text.find("SVZ_APK_URL", first), text.find("tools/build.sh", first)) if i >= 0)
            self.assertTrue(0 <= guard < build, f"{os.path.basename(wf)}: private-repository guard must come before the build")

    def test_artifacts_expire(self):
        for wf in PUBLIC_WORKFLOWS + [PRIVATE_WORKFLOW]:
            text = read(wf)
            for m in re.finditer(r"uses: actions/upload-artifact@[^\n]*\n((?:\s{8,}[^\n]*\n)*)", text):
                block = m.group(1)
                days = re.search(r"retention-days:\s*(\d+)", block)
                self.assertTrue(days and int(days.group(1)) <= 7, f"{os.path.basename(wf)}: upload-artifact without short retention")

    @unittest.skipIf(yaml is None, "PyYAML not installed")
    def test_valid_yaml(self):
        for wf in PUBLIC_WORKFLOWS + [PRIVATE_WORKFLOW]:
            doc = yaml.safe_load(read(wf))
            self.assertIn("jobs", doc, wf)


class PrivateWorkflow(unittest.TestCase):
    def setUp(self):
        self.text = read(PRIVATE_WORKFLOW)

    def test_guard_is_first_step(self):
        first_step = self.text.index("    steps:\n") + len("    steps:\n")
        self.assertIn(PRIVATE_GUARD, self.text[first_step:first_step + 300])

    def test_actions_pinned_to_commits(self):
        for wf in [PRIVATE_WORKFLOW] + PUBLIC_WORKFLOWS:
            if os.path.basename(wf) == "build.yml":
                continue  # upstream workflow, kept as close to upstream as possible
            for m in re.finditer(r"uses:\s*([\w.-]+/[\w.-]+)@(\S+)", read(wf)):
                self.assertRegex(m.group(2), r"^[0-9a-f]{40}$", f"{os.path.basename(wf)}: {m.group(1)} not pinned")

    def test_secrets_only_through_env(self):
        """Secrets are passed as environment variables, never pasted into a script."""
        if yaml is None:
            self.skipTest("PyYAML not installed")
        for st in steps(yaml.safe_load(self.text)):
            self.assertNotIn("secrets.", st.get("run", ""), st.get("name"))

    def test_original_checked_and_cleaned(self):
        self.assertIn("sha256sum -c", self.text)
        self.assertIn("SVZ_EXPECT_CERT_SHA256", self.text)
        self.assertRegex(self.text, r"if: always\(\)\n\s+run: rm -rf engine/keys engine/original")


class NoGameFilesCommitted(unittest.TestCase):
    def test_tracked_files(self):
        try:
            files = subprocess.run(["git", "-C", ROOT, "ls-files"], capture_output=True, text=True, check=True).stdout.split()
        except (OSError, subprocess.CalledProcessError):
            self.skipTest("not a git checkout")
        bad_ext = re.compile(r"\.(apk|obb|jks|keystore|p12|aab|assets|resS|resource)$", re.I)
        bad_dir = re.compile(r"^(original|work|keys|build|godot/(data|tex|audio|video|src/Game|src/UnityEngine))/")
        offenders = [f for f in files if bad_ext.search(f) or bad_dir.match(f)]
        self.assertEqual(offenders, [])


if __name__ == "__main__":
    unittest.main()
