#!/usr/bin/env python3
"""Validate spec/parity.csv and print a count per status and per phase.

Usage: python tools/parity-check.py [path]   (default: spec/parity.csv)
Checks: exact header, unique non-empty ids, rust_status in the allowed set,
phase an integer in ALLOWED_PHASES (ROADMAP.md phases 0-7; 0 also marks rows with
nothing left to schedule), non-empty feature, every row has the full column
count, and status done => a non-empty rust_call_site.
Exit code 0 when valid, 1 otherwise.
"""
import csv
import re
import sys
from collections import Counter

HEADER = ["id", "area", "feature", "bulwark_ref", "rust_status",
          "rust_call_site", "csharp_spec", "phase", "size"]
STATUSES = ["done", "partial", "missing", "unverified"]
ALLOWED_PHASES = [0, 1, 2, 3, 4, 5, 6, 7]


def main(path):
    errors = []
    with open(path, encoding="utf-8-sig", newline="") as f:
        rows = list(csv.reader(f))
    if not rows or rows[0] != HEADER:
        print(f"{path}: header must be {','.join(HEADER)}")
        return 1
    seen = {}
    by_status, by_phase = Counter(), Counter()
    for n, row in enumerate(rows[1:], 2):
        if len(row) != len(HEADER):
            errors.append(f"line {n}: {len(row)} columns, expected {len(HEADER)}")
            continue
        r = dict(zip(HEADER, row))
        row_id = r["id"].strip()
        if not row_id:
            errors.append(f"line {n}: empty id")
        elif row_id in seen:
            errors.append(f"line {n}: duplicate id {row_id} (first on line {seen[row_id]})")
        else:
            seen[row_id] = n
        if not r["feature"].strip():
            errors.append(f"line {n}: empty feature ({row_id})")
        if r["rust_status"] not in STATUSES:
            errors.append(f"line {n}: rust_status {r['rust_status']!r} not in {STATUSES} ({row_id})")
        else:
            by_status[r["rust_status"]] += 1
        if r["rust_status"] == "done" and not r["rust_call_site"].strip():
            errors.append(f"line {n}: status done needs a rust_call_site ({row_id})")
        phase = r["phase"].strip()
        if re.fullmatch(r"[0-9]+", phase) and int(phase) in ALLOWED_PHASES:
            by_phase[int(phase)] += 1
        else:
            errors.append(f"line {n}: phase {r['phase']!r} must be one of {ALLOWED_PHASES} ({row_id})")
    for e in errors:
        print(e)
    print(f"rows: {len(rows) - 1}")
    print("by status: " + ", ".join(f"{s}={by_status[s]}" for s in STATUSES))
    print("by phase:  " + ", ".join(f"{p}={by_phase[p]}" for p in ALLOWED_PHASES))
    print("OK" if not errors else f"{len(errors)} error(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "spec/parity.csv"))
