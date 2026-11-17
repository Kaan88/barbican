#!/usr/bin/env python3
"""Compare a JMAP method trace of the C# client with the sequence derived from the Rust app (PORT-PLAN.md §3.4 "Trace", §4.6).

Both sides are BARBICAN_JMAP_TRACE files: one line per API request, `Email/query(limit=50),Email/get -> Email/query,Email/get`
(Rust jmap.rs:82 writes the same, prefixed with a time), plus `PUSH ...` lines. Only method names are compared, never data.

Expected-file syntax (spec/traces/M1/*.rust.txt, written by hand from the Rust source):
  # text              a comment
  line   # file:line  the citation after "  #" is ignored
  ~line               optional: allowed in the C# trace, not required (another stream's or milestone's work, a race)
  @phase name         a barrier: every line of a phase comes after every line of the phases before it
  @thread name        lines of one thread keep their order; threads of one phase may interleave
The C# trace (spec/traces/M1/*.cs.txt) marks its phases with the same `@phase name` lines.

Ignored on both sides: times, SESSION / RETRY / DOWNLOAD lines, PUSH ping and the stream lifecycle (open, closed, silent, dropped,
failed); `PUSH state {json}` (Rust) and `PUSH state A,B` (C#) both become `PUSH state A,B` with the type names sorted.

Usage: tools/trace-diff.py EXPECTED.rust.txt ACTUAL.cs.txt   (exit 0 when they match, 1 with a report when not)
       tools/trace-diff.py --all spec/traces/M1              (every <name>.rust.txt against its <name>.cs.txt)
"""

from __future__ import annotations

import json
import re
import sys
from collections import Counter
from pathlib import Path

TIME = re.compile(r"^\d{2}:\d{2}:\d{2}(\.\d+)?\s+")
IGNORED = re.compile(r"^(SESSION|RETRY|DOWNLOAD)\b|^PUSH (ping|open|closed|silent|dropped|failed)\b")


def normalise(line: str) -> str | None:
    line = TIME.sub("", line.strip())
    if not line or IGNORED.match(line):
        return None
    if line.startswith("PUSH state "):
        rest = line[len("PUSH state "):].strip()
        if rest.startswith("{"):
            try:
                changed = json.loads(rest)
                types = sorted({t for per_account in changed.values() for t in per_account})
            except (ValueError, AttributeError):
                types = []
        else:
            types = sorted(t for t in rest.split(",") if t)
        return "PUSH state " + ",".join(types)
    return line


def parse_expected(text: str):
    """[(phase, [(thread, line, optional)])]"""
    phases: list[tuple[str, list[tuple[str, str, bool]]]] = [("start", [])]
    thread = "main"
    for raw in text.splitlines():
        body = raw.split("  #", 1)[0].rstrip()
        stripped = body.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if stripped.startswith("@phase "):
            phases.append((stripped[7:].strip(), []))
            thread = "main"
            continue
        if stripped.startswith("@thread "):
            thread = stripped[8:].strip()
            continue
        optional = stripped.startswith("~")
        line = normalise(stripped.lstrip("~").strip())
        if line is not None:
            phases[-1][1].append((thread, line, optional))
    return [p for p in phases if p[1] or p[0] != "start"]


def parse_actual(text: str):
    phases: list[tuple[str, list[str]]] = [("start", [])]
    for raw in text.splitlines():
        stripped = raw.strip()
        if stripped.startswith("@phase "):
            phases.append((stripped[7:].strip(), []))
            continue
        if stripped.startswith("#"):
            continue
        line = normalise(stripped)
        if line is not None:
            phases[-1][1].append(line)
    return [p for p in phases if p[1] or p[0] != "start"]


def is_subsequence(needles: list[str], hay: list[str]) -> bool:
    it = iter(hay)
    return all(any(n == h for h in it) for n in needles)


def compare(expected_text: str, actual_text: str) -> list[str]:
    expected = parse_expected(expected_text)
    actual = parse_actual(actual_text)
    problems: list[str] = []
    names_e = [p[0] for p in expected]
    names_a = [p[0] for p in actual]
    if names_e != names_a:
        return [f"phases differ: expected {names_e}, got {names_a}"]
    for (phase, lines), (_, got) in zip(expected, actual):
        required = Counter(line for _, line, opt in lines if not opt)
        allowed = Counter(line for _, line, _ in lines)
        have = Counter(got)
        for line, n in sorted((required - have).items()):
            problems.append(f"[{phase}] missing {n}x: {line}")
        for line, n in sorted((have - allowed).items()):
            problems.append(f"[{phase}] unexpected {n}x: {line}")
        for thread in dict.fromkeys(t for t, _, _ in lines):
            order = [line for t, line, opt in lines if t == thread and not opt]
            if not is_subsequence(order, got):
                problems.append(f"[{phase}] thread {thread}: order differs: expected {order}")
    return problems


def main(argv: list[str]) -> int:
    if len(argv) == 3 and argv[1] == "--all":
        folder = Path(argv[2])
        pairs = [(e, e.with_name(e.name.replace(".rust.txt", ".cs.txt"))) for e in sorted(folder.glob("*.rust.txt"))]
    elif len(argv) == 3:
        pairs = [(Path(argv[1]), Path(argv[2]))]
    else:
        print(__doc__)
        return 2
    failed = False
    for exp, act in pairs:
        if not act.exists():
            print(f"{act}: missing")
            failed = True
            continue
        problems = compare(exp.read_text(encoding="utf-8"), act.read_text(encoding="utf-8"))
        required = sum(1 for _, lines in parse_expected(exp.read_text(encoding="utf-8")) for _, _, o in lines if not o)
        if problems:
            failed = True
            print(f"{exp.name} vs {act.name}: DIFFERENT")
            for p in problems:
                print("  " + p)
        else:
            print(f"{exp.name} vs {act.name}: same ({required} required lines)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
