#!/usr/bin/env python3
"""spec-check: the catalogue's consistency and porting status (PORT-PLAN.md §2.3-§2.5, §4.3).

Usage (from anywhere in the repo; Python 3.10+, stdlib only):
  python tools/spec-check.py                 # summary: T- rows per milestone (ported / pending / dropped)
  python tools/spec-check.py --milestone M1  # also list M1's unmapped T- rows; exit 1 if any remain
  python tools/spec-check.py --write         # write C# test names and `ported` into spec/rust-tests.csv
  python tools/spec-check.py --strict        # exit 1 on catalogue errors (duplicate ids, dangling refs)

What it checks:
  * every [RustTest("module::name")] in tests/**/*.cs names a row of spec/rust-tests.csv (rust_test_tag);
    a tag that matches no row, or a row claimed by two tests, is an error;
  * a T- row counts as done when a C# test carries its tag, or its status is `dropped(D-###...)`;
  * catalogue ids (B-, D-, G-, I-, J-, L-, T-, MENU-) are defined once, and references to them resolve;
  * the plugin hook catalogue (spec/plugins/hooks.csv): rows per milestone and how many have a call site
    (a `HookNames.<Name>` reference under src/, as Bulwark's plugin-hook-catalog test counts), and
    the red tests still marked [Red] (tests that are expected to fail until their stream lands).
The milestone gate (§2.5 first line) is `--milestone M<n>` exiting 0.
"""
from __future__ import annotations

import argparse
import csv
import io
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SPEC = ROOT / "spec"
TESTS_CSV = SPEC / "rust-tests.csv"
ATTR = re.compile(r'\[RustTest\("([^"]+)"\)\]')
METHOD = re.compile(r"(?:public|internal|private)?\s*(?:static\s+)?(?:async\s+)?[\w<>\[\],? ]+\s+(\w+)\s*\(")
CLASS = re.compile(r"\b(?:class|record)\s+(\w+)")
NAMESPACE = re.compile(r"^\s*namespace\s+([\w.]+)", re.M)
# Ids defined by a catalogue file: first cell of a markdown table row, or first CSV column.
DEF_MD = re.compile(r"^\|\s*((?:B-[A-Z]+|D|G|I|J|L|T)-\d{3,})\s*\|", re.M)
DEF_MENU = re.compile(r"^## (MENU-[A-Z-]+)", re.M)
REF = re.compile(r"\b((?:B-[A-Z]+|D|G|I|J|L|T)-\d{3,})\b")


def read_rows() -> tuple[list[str], list[dict[str, str]]]:
    with TESTS_CSV.open(encoding="utf-8", newline="") as f:
        r = csv.DictReader(f)
        return list(r.fieldnames or []), list(r)


def tag_of(row: dict[str, str]) -> str:
    m = ATTR.search(row.get("rust_test_tag", ""))
    return m.group(1) if m else f"{row['module']}::{row['rust_test']}"


def scan_tests() -> dict[str, list[str]]:
    """RustTest tag -> ["Namespace.Class.Method (path:line)", ...]."""
    found: dict[str, list[str]] = defaultdict(list)
    for p in sorted((ROOT / "tests").rglob("*.cs")):
        if any(part in ("bin", "obj") for part in p.parts):
            continue
        text = p.read_text(encoding="utf-8-sig")
        ns = NAMESPACE.search(text)
        lines = text.splitlines()
        cls = ""
        for i, line in enumerate(lines):
            c = CLASS.search(line)
            if c and "[" not in line.split("class")[0]:
                cls = c.group(1)
            m = ATTR.search(line)
            if not m:
                continue
            name = "?"
            for j in range(i + 1, min(i + 6, len(lines))):
                mm = METHOD.search(lines[j])
                if mm and not lines[j].lstrip().startswith("["):
                    name = mm.group(1)
                    break
            full = ".".join(x for x in ((ns.group(1) if ns else ""), cls, name) if x)
            found[m.group(1)].append(f"{full} ({p.relative_to(ROOT).as_posix()}:{i + 1})")
    return found


def catalogue_ids() -> tuple[Counter[str], set[str], list[str]]:
    defined: Counter[str] = Counter()
    refs: set[str] = set()
    where: dict[str, str] = {}
    for p in sorted(SPEC.rglob("*")):
        if p.suffix not in (".md", ".csv") or "feel" in p.parts:
            continue
        text = p.read_text(encoding="utf-8-sig")
        if p.suffix == ".csv":
            rows = list(csv.reader(io.StringIO(text)))
            for row in rows[1:]:
                if row and re.fullmatch(r"[A-Z]-\d{3,}", row[0]):
                    defined[row[0]] += 1
                    where.setdefault(row[0], p.name)
                refs.update(REF.findall(",".join(row[1:])))
        else:
            for m in DEF_MD.finditer(text):
                defined[m.group(1)] += 1
                where.setdefault(m.group(1), p.name)
            for m in DEF_MENU.finditer(text):
                defined[m.group(1)] += 1
            for line in text.splitlines():
                cells = line.split("|")
                body = "|".join(cells[2:]) if line.startswith("|") else line
                refs.update(REF.findall(body))
    dangling = sorted(r for r in refs if r not in defined)
    return defined, refs, dangling


def hooks_summary() -> list[str]:
    """Rows of spec/plugins/hooks.csv per milestone, and how many are fired somewhere under src/."""
    path = SPEC / "plugins" / "hooks.csv"
    if not path.exists():
        return []
    with path.open(encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))
    used: set[str] = set()
    ref = re.compile(r"HookNames\.(\w+)")
    for p in (ROOT / "src").rglob("*.cs"):
        if p.name.endswith(".g.cs") or any(part in ("bin", "obj") for part in p.parts):
            continue
        used.update(ref.findall(p.read_text(encoding="utf-8-sig")))
    per: dict[str, list[int]] = defaultdict(lambda: [0, 0])
    for r in rows:
        name = r["name"][0].upper() + r["name"][1:]
        per[r["milestone"]][0] += 1
        if name in used:
            per[r["milestone"]][1] += 1
    out = [f"plugin hooks (spec/plugins/hooks.csv): {len(rows)} hooks in {len({r['group'] for r in rows})} groups",
           f"  {'milestone':<10}{'hooks':>6}{'fired':>7}"]
    for ms in sorted(per, key=lambda m: (int(re.sub(r"\D", "", m)) if re.sub(r"\D", "", m) else 99, m)):
        out.append(f"  {ms:<10}{per[ms][0]:>6}{per[ms][1]:>7}")
    slots = SPEC / "plugins" / "slots.csv"
    if slots.exists():
        with slots.open(encoding="utf-8", newline="") as f:
            out.append(f"plugin slots (spec/plugins/slots.csv): {sum(1 for _ in csv.DictReader(f))} catalogued")
    return out


def red_tests() -> list[str]:
    """Tests still marked [Red] (expected red until their stream turns them green)."""
    n: Counter[str] = Counter()
    pat = re.compile(r'\[Red\("([^"]+)"\)\]')
    for p in sorted((ROOT / "tests").rglob("*.cs")):
        if any(part in ("bin", "obj") for part in p.parts):
            continue
        n.update(pat.findall(p.read_text(encoding="utf-8-sig")))
    if not n:
        return []
    return ["red tests ([Red(\"stream\")], excluded from the default run): " + ", ".join(f"{k} {v}" for k, v in sorted(n.items()))]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--milestone", help="gate: list and fail on unmapped T- rows of this milestone (e.g. M1)")
    ap.add_argument("--write", action="store_true", help="write csharp_test/status into spec/rust-tests.csv")
    ap.add_argument("--strict", action="store_true", help="fail on catalogue errors too")
    a = ap.parse_args()

    fields, rows = read_rows()
    by_tag = {tag_of(r): r for r in rows}
    found = scan_tests()
    errors: list[str] = []

    for tag, uses in sorted(found.items()):
        if tag not in by_tag:
            errors.append(f"[RustTest(\"{tag}\")] matches no row of spec/rust-tests.csv: {uses[0]}")
        elif len(uses) > 1:
            errors.append(f"[RustTest(\"{tag}\")] is on {len(uses)} tests: {'; '.join(uses)}")

    per: dict[str, Counter[str]] = defaultdict(Counter)
    open_rows: dict[str, list[dict[str, str]]] = defaultdict(list)
    for r in rows:
        ms = r["milestone"] or "?"
        tag = tag_of(r)
        if tag in found:
            state = "ported"
            if a.write:
                r["csharp_test"] = found[tag][0].split(" (")[0]
                r["status"] = "ported"
        elif r["status"].startswith("dropped(D-"):
            state = "dropped"
        else:
            state = "pending"
            open_rows[ms].append(r)
        per[ms][state] += 1

    defined, _refs, dangling = catalogue_ids()
    dups = sorted(k for k, v in defined.items() if v > 1)
    cat_errors = [f"id defined more than once: {d}" for d in dups] + [f"dangling reference: {d}" for d in dangling]

    def key(m: str) -> tuple[int, str]:
        n = re.sub(r"\D", "", m)
        return (int(n) if n else 99, m)

    print("spec-check: Rust tests (spec/rust-tests.csv) by milestone")
    print(f"  {'milestone':<10}{'rows':>6}{'ported':>8}{'dropped':>9}{'pending':>9}")
    tot: Counter[str] = Counter()
    for ms in sorted(per, key=key):
        c = per[ms]
        tot.update(c)
        print(f"  {ms:<10}{sum(c.values()):>6}{c['ported']:>8}{c['dropped']:>9}{c['pending']:>9}")
    print(f"  {'total':<10}{sum(tot.values()):>6}{tot['ported']:>8}{tot['dropped']:>9}{tot['pending']:>9}")
    print(f"catalogue: {len(defined)} ids defined; {len(dups)} duplicate; {len(dangling)} dangling references")
    for line in hooks_summary() + red_tests():
        print(line)
    for e in errors:
        print(f"ERROR {e}")
    for e in cat_errors:
        print(f"{'ERROR' if a.strict else 'warn '} {e}")

    if a.write:
        with TESTS_CSV.open("w", encoding="utf-8", newline="") as f:
            w = csv.DictWriter(f, fieldnames=fields, lineterminator="\n")
            w.writeheader()
            w.writerows(rows)
        print(f"wrote {TESTS_CSV.relative_to(ROOT).as_posix()}")

    gate_failed = False
    if a.milestone:
        left = open_rows.get(a.milestone, [])
        print(f"{a.milestone}: {len(left)} T- rows without a C# test or a D- entry")
        for r in left:
            print(f"  {r['id']} {tag_of(r)}")
        gate_failed = bool(left)

    if errors or gate_failed or (a.strict and cat_errors):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
