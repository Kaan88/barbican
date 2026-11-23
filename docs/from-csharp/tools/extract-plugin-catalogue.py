#!/usr/bin/env python3
"""Extract spec/plugins/hooks.csv and slots.csv from Bulwark's plugin catalogues (re-runnable).

  python tools/extract-plugin-catalogue.py <bulwark-root>

Reads <bulwark>/lib/plugin-hook-catalog.ts and lib/plugin-slot-catalog.ts. Columns that people edit
later (milestone, owner, cs_call_site, status, budget_ms, fail_closed) are kept from the existing CSV when the row's name
is already there; new rows get the defaults from tools/plugin_milestones.py (spec/plugins/README.md).
"""
import csv
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "spec" / "plugins"


def split_args(s):
    """Split a call's argument text at top-level commas (strings, brackets and braces respected)."""
    args, depth, cur, q, i = [], 0, [], None, 0
    while i < len(s):
        c = s[i]
        if q:
            cur.append(c)
            if c == "\\":
                i += 1
                cur.append(s[i])
            elif c == q:
                q = None
        elif c in "'\"`":
            q = c
            cur.append(c)
        elif c in "([{":
            depth += 1
            cur.append(c)
        elif c in ")]}":
            depth -= 1
            cur.append(c)
        elif c == "," and depth == 0:
            args.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
        i += 1
    if "".join(cur).strip():
        args.append("".join(cur).strip())
    return args


def unq(s):
    s = s.strip()
    if s[:1] in "'\"`" and s[-1:] == s[:1]:
        return re.sub(r"\\(.)", r"\1", s[1:-1])
    return s


def call_text(text, start):
    """Text between the parentheses of the call whose '(' is at text[start]."""
    depth, q, i = 0, None, start
    while i < len(text):
        c = text[i]
        if q:
            if c == "\\":
                i += 1
            elif c == q:
                q = None
        elif c in "'\"`":
            q = c
        elif c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return text[start + 1:i]
        i += 1
    raise ValueError("unbalanced parentheses")


def entries(path, fn):
    """Yield (name, line, call args, tail) for every `name: fn(...)` / `name: { ...fn(...), ... }` entry."""
    text = path.read_text(encoding="utf-8")
    pat = re.compile(r"^  ('?[\w-]+'?): (?:\{ \.\.\.)?" + fn + r"\(", re.M)
    for m in pat.finditer(text):
        open_at = m.end() - 1
        args = split_args(call_text(text, open_at))
        line = text.count("\n", 0, m.start()) + 1
        eol = text.find("\n", m.end())
        yield unq(m.group(1)), line, args, text[open_at:eol]


def existing(path, key):
    if not path.exists():
        return {}
    with path.open(encoding="utf-8", newline="") as f:
        return {r[key]: r for r in csv.DictReader(f)}


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    bw = Path(sys.argv[1])
    sys.path.insert(0, str(Path(__file__).parent))
    from plugin_milestones import NA_STATUS, hook_milestone, hook_owner, slot_milestone

    OUT.mkdir(parents=True, exist_ok=True)
    hooks_path, slots_path = OUT / "hooks.csv", OUT / "slots.csv"
    old_h, old_s = existing(hooks_path, "name"), existing(slots_path, "name")
    hcols = ["name", "group", "kind", "permission", "also_permission", "payload", "when", "bulwark_ref",
             "milestone", "owner", "cs_call_site", "status", "budget_ms", "fail_closed"]
    rows = []
    for name, line, a, tail in entries(bw / "lib" / "plugin-hook-catalog.ts", "H"):
        group, kind, perm, payload, when = (unq(x) for x in a[:5])
        also = ""
        m = re.search(r"alsoPermission: '([^']+)'", tail)
        if m:
            also = m.group(1)
        if "REMINDER_TAKEOVER" in tail:
            also = "tasks:write"
        elif "REMINDER" in tail:
            also = "tasks:read"
        r = old_h.get(name, {})
        rows.append({
            "name": name, "group": group, "kind": kind, "permission": perm, "also_permission": also,
            "payload": payload, "when": when, "bulwark_ref": f"lib/plugin-hook-catalog.ts:{line}",
            "milestone": r.get("milestone") or hook_milestone(name, group),
            "owner": r.get("owner") or hook_owner(name, group),
            "cs_call_site": r.get("cs_call_site", ""), "status": r.get("status") or NA_STATUS.get(name, "pending"),
            # R2-10: Bulwark lib/plugin-hooks.ts:62-66 (5 s, intercepts 60 s, takeovers fail closed); edited rows are kept
            "budget_ms": r.get("budget_ms") or {"intercept": "60000", "takeover": "600000"}.get(kind, "5000"),
            "fail_closed": r.get("fail_closed") or ("true" if kind == "takeover" else "false"),
        })
    with hooks_path.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, hcols, lineterminator="\n")
        w.writeheader()
        w.writerows(rows)

    scols = ["name", "area", "kind", "permissions", "props", "where", "bulwark_ref", "milestone", "status"]
    srows = []
    for name, line, a, tail in entries(bw / "lib" / "plugin-slot-catalog.ts", "S"):
        area, kind, perms, props, where = unq(a[0]), unq(a[1]), a[2], a[3], unq(a[4])
        perms = ";".join(re.findall(r"'([^']+)'", perms))
        props = re.sub(r"\s+", " ", props) if props.startswith("{") else unq(props)
        r = old_s.get(name, {})
        srows.append({
            "name": name, "area": area, "kind": kind, "permissions": perms, "props": props, "where": where,
            "bulwark_ref": f"lib/plugin-slot-catalog.ts:{line}",
            "milestone": r.get("milestone") or slot_milestone(name, area),
            "status": r.get("status") or "catalogued",
        })
    with slots_path.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, scols, lineterminator="\n")
        w.writeheader()
        w.writerows(srows)
    groups = {r["group"] for r in rows}
    print(f"hooks: {len(rows)} in {len(groups)} groups; slots: {len(srows)}")


if __name__ == "__main__":
    main()
