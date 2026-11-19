#!/usr/bin/env python3
"""Regression for the narrowly scoped, mandatory upstream font attribution."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

source = Path(__file__).resolve().parent / "privacy-check.sh"
root = Path(tempfile.mkdtemp(prefix="barbican-privacy-font-"))
assert root.resolve().parent == Path(tempfile.gettempdir()).resolve()
bash = shutil.which("bash") or r"C:/Program Files/Git/bin/bash.exe"

def run(*args, ok=True):
    result = subprocess.run(args, cwd=root, text=True, capture_output=True, check=False)
    if ok and result.returncode != 0:
        raise AssertionError(result.stdout + result.stderr)
    return result

try:
    run("git", "init", "-q")
    run("git", "config", "user.name", "Privacy Test")
    run("git", "config", "user.email", "test@users.noreply.github.com")
    (root / ".gitignore").write_text(".local/\n", encoding="utf-8")
    (root / ".local").mkdir()
    (root / ".local/privacy-patterns.txt").write_text("privacy_probe_secret\n", encoding="utf-8")
    (root / "tools").mkdir()
    shutil.copy2(source, root / "tools/privacy-check.sh")
    license_file = root / "src/Barbican.App/Assets/Fonts/OpenDyslexic-OFL.txt"
    license_file.parent.mkdir(parents=True)
    contact = "support" + "@" + "abbiecod.es"
    license_file.write_text(contact + "\n", encoding="utf-8")
    run("git", "add", ".gitignore", "tools/privacy-check.sh", str(license_file.relative_to(root)))
    run("git", "commit", "-qm", "font notice")
    run(bash, "tools/privacy-check.sh", "--worktree")
    run(bash, "tools/privacy-check.sh", "HEAD")
    other = root / "other.txt"
    other.write_text(contact + "\n", encoding="utf-8")
    assert run(bash, "tools/privacy-check.sh", "--worktree", ok=False).returncode != 0
    run("git", "add", "other.txt")
    run("git", "commit", "-qm", "other file")
    assert run(bash, "tools/privacy-check.sh", "HEAD", ok=False).returncode != 0
    run("git", "rm", "-q", "other.txt")
    run("git", "commit", "-qm", "remove fixture")
    license_file.write_text(contact + "\nprivacy_probe_secret\n", encoding="utf-8")
    assert run(bash, "tools/privacy-check.sh", "--worktree", ok=False).returncode != 0
    print("privacy font regression: passed (worktree and revision acceptance, other-file and hard-pattern rejection)")
finally:
    def remove_readonly(function, path, error):
        os.chmod(path, 0o700)
        function(path)
    shutil.rmtree(root, onexc=remove_readonly)
