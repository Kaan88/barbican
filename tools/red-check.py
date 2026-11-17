#!/usr/bin/env python3
"""red-check: prove the [Red] tests are red for the right reason (M1-plan F2).

  python tools/red-check.py            build, run only the [Red] tests, check every one failed with PortPending
  python tools/red-check.py --list     list the [Red] tests per stream (no build)

A [Red("stream")] test is excluded from the default run (tests/Directory.Build.props), so `tools/build.ps1 test` stays
green. This script runs them with BARBICAN_RED=1 through tools/build.ps1 (never dotnet directly) and checks:
  * every red test FAILED (one that passes is reported: "green, remove [Red]");
  * every failure is "Port pending: ..." (a contract stub that throws PortPendingException) or a call-site assertion
    ("without a call site"), not a compile error, a NullReference or any other accident;
  * the number of failures equals the number of [Red] attributes in tests/.
Exit 0 when all of that holds. A stream that implements its stubs removes the [Red] attributes in the same commit.
"""
import os
import re
import subprocess
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RED = re.compile(r'\[Red\("([^"]+)"\)\]')
RIGHT_REASONS = ("Port pending:", "without a call site")


def red_attributes():
    n = Counter()
    for p in sorted((ROOT / "tests").rglob("*.cs")):
        if any(part in ("bin", "obj") for part in p.parts):
            continue
        n.update(RED.findall(p.read_text(encoding="utf-8-sig")))
    return n


def main():
    per_stream = red_attributes()
    total = sum(per_stream.values())
    if "--list" in sys.argv:
        for k, v in sorted(per_stream.items()):
            print(f"{k:<10}{v:>4}")
        print(f"{'total':<10}{total:>4}")
        return 0

    env = dict(os.environ, BARBICAN_RED="1")
    ps = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(ROOT / "tools" / "build.ps1"), "test"]
    out = subprocess.run(ps, cwd=ROOT, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace")
    lines = (out.stdout + "\n" + out.stderr).splitlines()
    if any("error CS" in line or "error MA" in line or "Build FAILED" in line for line in lines):
        print("red-check: the solution does not build, so the red tests cannot run:")
        print("\n".join(line for line in lines if "error " in line)[:4000])
        return 1

    failed, green, wrong = [], [], []
    i = 0
    while i < len(lines):
        m = re.match(r"^(failed|passed) (\S+)", lines[i])
        if m:
            name = m.group(2)
            j = i + 1
            block = []
            while j < len(lines) and not re.match(r"^(failed|passed|skipped) \S+", lines[j]) and not lines[j].startswith("Test run"):
                block.append(lines[j])
                j += 1
            if m.group(1) == "passed":
                green.append(name)
            else:
                failed.append(name)
                text = "\n".join(block)
                if not any(r in text for r in RIGHT_REASONS):
                    wrong.append((name, next((b.strip() for b in block if b.strip() and not b.strip().startswith("from ")), "")))
            i = j
        else:
            i += 1

    print(f"red-check: {total} [Red] attributes ({', '.join(f'{k} {v}' for k, v in sorted(per_stream.items()))})")
    print(f"  failed {len(failed)}, passed {len(green)}, failed for another reason {len(wrong)}")
    for g in green:
        print(f"  GREEN, remove [Red]: {g}")
    for n, why in wrong:
        print(f"  WRONG REASON: {n}: {why[:200]}")
    ok = not green and not wrong and len(failed) == total
    if len(failed) != total and not green and not wrong:
        print(f"  expected {total} failures, saw {len(failed)}")
    print("red-check: OK, every red test fails for the right reason" if ok else "red-check: FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
