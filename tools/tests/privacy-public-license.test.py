"""Verify the public-license exception is content- and path-bound, in isolated Git repos."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
NOTICE = Path("tools/plugin-runtime-build/third-party-licenses/clsx--license")
BASH = shutil.which("bash") or "C:/Program Files/Git/bin/bash.exe"

class PublicLicensePrivacyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="barbican-privacy-")
        self.root = Path(self.temp.name)
        self.git("init", "-q")
        self.git("config", "user.name", "Fixture")
        self.git("config", "user.email", "12345+fixture@users.noreply.github.com")
        (self.root / ".gitignore").write_text(".local/\n", encoding="utf-8")
        (self.root / ".local").mkdir()
        (self.root / ".local/privacy-patterns.txt").write_text("DO_NOT_PUBLISH_FIXTURE\n", encoding="utf-8")
        (self.root / "tools").mkdir()
        shutil.copyfile(ROOT / "tools/privacy-check.sh", self.root / "tools/privacy-check.sh")
        (self.root / NOTICE).parent.mkdir(parents=True)
        shutil.copyfile(ROOT / NOTICE, self.root / NOTICE)
        self.git("add", ".")
        self.git("commit", "-qm", "Synthetic baseline")
    def tearDown(self):
        self.assertEqual(Path(tempfile.gettempdir()).resolve(), self.root.resolve().parent)
        self.assertTrue(self.root.name.startswith("barbican-privacy-"))
        self.temp.cleanup()
    def git(self, *args):
        return subprocess.run(["git", *args], cwd=self.root, capture_output=True, check=True)
    def scan(self, revision="--worktree"):
        return subprocess.run([BASH, "tools/privacy-check.sh", revision], cwd=self.root,
                              capture_output=True, text=True, env=dict(os.environ, LC_ALL="C"))
    def test_exact_upstream_notice_passes_worktree_and_revision(self):
        for revision in ("--worktree", "HEAD"):
            result = self.scan(revision)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
    def test_modified_notice_is_not_exempted(self):
        with (self.root / NOTICE).open("a", encoding="utf-8") as f:
            f.write("DO_NOT_PUBLISH_FIXTURE\n")
        result = self.scan()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("reviewed public license content changed", result.stdout + result.stderr)
    def test_modified_committed_notice_is_not_exempted(self):
        with (self.root / NOTICE).open("a", encoding="utf-8") as f:
            f.write("Modified notice.\n")
        self.git("add", ".")
        self.git("commit", "-qm", "Synthetic edited notice")
        self.assertNotEqual(0, self.scan("HEAD").returncode)
    def test_same_notice_at_another_path_is_not_exempted(self):
        shutil.copyfile(self.root / NOTICE, self.root / "copied-notice.txt")
        self.assertNotEqual(0, self.scan().returncode)
    def test_unrelated_private_data_remains_blocked(self):
        (self.root / "unrelated.txt").write_text("DO_NOT_PUBLISH_FIXTURE\n", encoding="utf-8")
        self.assertNotEqual(0, self.scan().returncode)

if __name__ == "__main__":
    unittest.main()
