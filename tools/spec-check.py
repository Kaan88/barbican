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
  * a T- row counts as done when a C# test that is not `[Red]` carries its tag, or its status is `dropped(D-###...)`;
    a row whose only tests are `[Red]` (excluded from the default run, so never proven) stays `pending` (Fable F-11);
  * catalogue ids (B-, D-, G-, I-, J-, L-, T-, MENU-) are defined once, and references to them resolve;
  * the plugin hook catalogue (spec/plugins/hooks.csv): rows per milestone and how many have a call site
    (a `HookNames.<Name>` reference under src/, as Bulwark's plugin-hook-catalog test counts), and
    the red tests still marked [Red] (tests that are expected to fail until their stream lands).
The milestone gate (§2.5 first line) is `--milestone M<n>` exiting 0: no unmapped or red-only T- row of M<n>, and no
`[Red]` test left anywhere (a red test is a promise of a stream that has not landed; at a gate none may remain).
J- rows whose `cs_status` is `done` need a test name in `cs_test`; `partial(M<k>)` is for a row only partly exercised.
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


RED = re.compile(r"\[Red\(")


def scan_tests() -> dict[str, list[str]]:
    """RustTest tag -> ["Namespace.Class.Method (path:line)", ...]; a test marked [Red] has " [Red]" after its location."""
    found: dict[str, list[str]] = defaultdict(list)
    for p in sorted((ROOT / "tests").rglob("*.cs")):
        if any(part in ("bin", "obj") for part in p.parts):
            continue
        text = p.read_text(encoding="utf-8-sig")
        ns = NAMESPACE.search(text)
        lines = text.splitlines()
        cls = ""
        cls_red = False
        for i, line in enumerate(lines):
            c = CLASS.search(line)
            if c and "[" not in line.split("class")[0]:
                cls = c.group(1)
                # [Red] on the class: the attribute lines just above it
                k = i - 1
                cls_red = False
                while k >= 0 and lines[k].lstrip().startswith("["):
                    cls_red = cls_red or bool(RED.search(lines[k]))
                    k -= 1
            m = ATTR.search(line)
            if not m:
                continue
            name = "?"
            red = cls_red
            # [Red] among the attribute lines above the RustTest line, or between it and the method
            k = i - 1
            while k >= 0 and lines[k].lstrip().startswith("["):
                red = red or bool(RED.search(lines[k]))
                k -= 1
            red = red or bool(RED.search(line))
            for j in range(i + 1, min(i + 6, len(lines))):
                red = red or bool(RED.search(lines[j]) and lines[j].lstrip().startswith("["))
                mm = METHOD.search(lines[j])
                if mm and not lines[j].lstrip().startswith("["):
                    name = mm.group(1)
                    break
            full = ".".join(x for x in ((ns.group(1) if ns else ""), cls, name) if x)
            found[m.group(1)].append(f"{full} ({p.relative_to(ROOT).as_posix()}:{i + 1})" + (" [Red]" if red else ""))
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
    """Count hooks per milestone and hooks with a static HookNames reference under src/."""
    path = SPEC / "plugins" / "hooks.csv"
    if not path.exists():
        return []
    with path.open(encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))
    used: set[str] = set()
    ref = re.compile(r"\bHookNames\.(\w+)")
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
    out = [f"plugin hooks (spec/plugins/hooks.csv): {len(rows)} hooks in {len({r['group'] for r in rows})} groups. Counts use static call-site references under src/",
           f"  {'milestone':<10}{'hooks':>6}{'hooks with refs':>17}"]
    for ms in sorted(per, key=lambda m: (int(re.sub(r"\D", "", m)) if re.sub(r"\D", "", m) else 99, m)):
        out.append(f"  {ms:<10}{per[ms][0]:>6}{per[ms][1]:>17}")
    slots = SPEC / "plugins" / "slots.csv"
    if slots.exists():
        with slots.open(encoding="utf-8", newline="") as f:
            out.append(f"plugin slots (spec/plugins/slots.csv): {sum(1 for _ in csv.DictReader(f))} catalogued")
    return out


def red_counts() -> Counter[str]:
    """Tests still marked [Red] by stream (expected red until their stream turns them green)."""
    n: Counter[str] = Counter()
    pat = re.compile(r'\[Red\("([^"]+)"\)\]')
    for p in sorted((ROOT / "tests").rglob("*.cs")):
        if any(part in ("bin", "obj") for part in p.parts) or p.name == "RedAttribute.cs":
            continue
        n.update(pat.findall(p.read_text(encoding="utf-8-sig")))
    return n


def red_tests() -> list[str]:
    n = red_counts()
    if not n:
        return []
    return ["red tests ([Red(\"stream\")], excluded from the default run): " + ", ".join(f"{k} {v}" for k, v in sorted(n.items()))]


def unproven_j_rows() -> list[str]:
    """J- rows that say `done` without naming the test that proves them (an empty cs_test)."""
    bad: list[str] = []
    for p in sorted(SPEC.glob("*.csv")):
        with p.open(encoding="utf-8", newline="") as f:
            for r in csv.DictReader(f):
                if r.get("cs_status") == "done" and not (r.get("cs_test") or "").strip():
                    bad.append(f"{r['id']} is done without a cs_test ({p.name})")
    return bad


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
        live = [u for u in found.get(tag, []) if not u.endswith(" [Red]")]
        if live:
            state = "ported"
            if a.write:
                r["csharp_test"] = live[0].split(" (")[0]
                r["status"] = "ported"
        elif r["status"].startswith("dropped(D-"):
            state = "dropped"
        else:
            # no test, or only [Red] ones: a red test is excluded from the default run, so it proves nothing (Fable F-11)
            state = "pending"
            open_rows[ms].append(dict(r, red_only="1" if tag in found else ""))
            if a.write and tag in found:
                r["csharp_test"] = found[tag][0].split(" (")[0].removesuffix(" [Red]")
                r["status"] = "pending"
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
        assigned = sum(per[a.milestone].values())
        print(f"{a.milestone} catalogue consistency: {assigned} assigned Rust test rows, "
              f"{len(left)} without a C# test mapping or recorded drop.")
        if a.milestone == "M6b":
            print("M6b flow acceptance and evidence: docs/milestones/M6b-plan.md#current-work")
        for r in left:
            print(f"  {r['id']} {tag_of(r)}{'  (only a [Red] test)' if r.get('red_only') else ''}")
        red = red_counts()
        if red:
            print(f"{a.milestone}: {sum(red.values())} [Red] tests left ({', '.join(f'{k} {v}' for k, v in sorted(red.items()))}); a gate needs none")
        unproven = unproven_j_rows()
        for u in unproven:
            print(f"  {u}")
        gate_failed = bool(left) or bool(red) or bool(unproven)

    if errors or gate_failed or (a.strict and cat_errors):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
