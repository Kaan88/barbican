"""Feel scenarios of stream search (M2 plan 6.4) against a hidden AUTOMATION run (tools/auto-run.ps1 start -Port 8884, BARBICAN_SAMPLE=1).

  search_type     five keystrokes at 80 ms: the suggestions follow each key, no rendered frame over 25 ms (frame probe)
  search_results  Enter: the old list stays until the results are ready, then one swap (row count never drops to 0 in between)
  chip_remove     removing a chip re-queries and swaps once

No input is produced: the commands drive the same model and services the views draw (`suggestions`, `search`, `chip-remove`).
Usage: python tools/feel-search.py --port 8884 [--out DIR]
A probe with no frames (display off, uniform capture) is reported "unproven (capture invalid)", never as a pass.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import autoconn  # noqa: E402


class App:
    def __init__(self, port):
        self.s, self.f = autoconn.connect(port, timeout=60)

    def cmd(self, c):
        self.f.write(c + "\n")
        self.f.flush()
        return self.f.readline().strip()


def gaps(frames):
    ends = sorted(e for _, e in frames)
    out = [b - a for a, b in zip(ends, ends[1:])]
    return out


def judge_frames(name, probe, limit_ms=25):
    frames = probe.get("frames", [])
    if len(frames) < 2:
        return {"scenario": name, "frames": len(frames), "verdict": "unproven (capture invalid: no frames)"}
    # a gap only counts inside a run of motion (frames less than 50 ms apart)
    g = gaps(frames)
    run = [x for x in g if x < 50]
    worst = max(run) if run else 0
    return {"scenario": name, "frames": len(frames), "worst_gap_ms": worst, "verdict": "ok" if worst <= limit_ms else f"FAIL gap {worst} ms > {limit_ms}"}


def rows_of(reply):
    # "ok N rows (total T) chips=C"
    parts = reply.split()
    return int(parts[1]) if len(parts) > 2 and parts[0] == "ok" and parts[1].isdigit() else -1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8884)
    ap.add_argument("--out", default=None)
    a = ap.parse_args()
    app = App(a.port)
    results = []

    # warm-up: first-use caches
    app.cmd("search from:alice")
    app.cmd("search")

    # search_type: five letters at 80 ms
    print(app.cmd("probe-start rendering"))
    stamps = []
    for text in ["a", "al", "ali", "alic", "alice"]:
        t0 = time.perf_counter()
        reply = app.cmd("suggestions " + text)
        stamps.append((text, round((time.perf_counter() - t0) * 1000, 1), reply[:60]))
        time.sleep(0.08)
    probe = json.loads(app.cmd("probe-stop") or "{}")
    r = judge_frames("search_type", probe)
    r["command_ms"] = [(t, ms) for t, ms, _ in stamps]
    results.append(r)

    # search_results: the old list stays until the results are ready, one swap
    app.cmd("search")
    before = rows_of(app.cmd("search report"))
    print(app.cmd("probe-start rendering"))
    t0 = time.perf_counter()
    reply = app.cmd("search from:alice has:attachment")
    took = round((time.perf_counter() - t0) * 1000, 1)
    probe = json.loads(app.cmd("probe-stop") or "{}")
    r = judge_frames("search_results", probe)
    r.update({"rows_before": before, "rows_after": rows_of(reply), "search_ms": took, "reply": reply})
    results.append(r)

    # chip_remove
    print(app.cmd("probe-start rendering"))
    t0 = time.perf_counter()
    reply = app.cmd("chip-remove has")
    took = round((time.perf_counter() - t0) * 1000, 1)
    probe = json.loads(app.cmd("probe-stop") or "{}")
    r = judge_frames("chip_remove", probe)
    r.update({"requery_ms": took, "reply": reply})
    results.append(r)

    print(json.dumps(results, indent=2))
    if a.out:
        os.makedirs(a.out, exist_ok=True)
        with open(os.path.join(a.out, "feel-search.json"), "w", encoding="utf8") as f:
            json.dump(results, f, indent=2)


if __name__ == "__main__":
    main()
