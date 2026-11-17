"""M2 verify feel scenarios (M2 plan section 8) on the hidden AUTOMATION build: the cross-stream transitions the streams could not
measure alone, in both styles and both themes.

  ctx_menu, ctx_menu_close     a message is open -> `menu-open MENU-MAIL 0`; then `key Escape`
  dialog, dialog_close         a message is open -> `dialog-open confirm` (the scrim over the body); then `dialog-answer cancel`
  palette_open, palette_close  a message is open -> `palette`; then `key Escape`
  delete, undo                 a message is open -> `act mail.delete`; then `act mail.undo` (sample FakeJmap only: no write leaves the process)

Usage (the app started with tools/auto-run.ps1 start -Port 8889 and BARBICAN_SAMPLE=1):
  python tools/feel-m2.py --port 8889 --dir artifacts/auto/8889/feel [--looks bento-light,bento-dark,nordic-light,nordic-dark] [--out R.json]

Each scenario records a WGC burst of the whole window (tools/flicker.py's summary) and, in a second run, a `rendering` probe (gaps
between rendered frames). A burst whose frames are one flat colour is invalid and reported as such, never as a pass; a probe with
fewer than two frames is "unproven (capture invalid)". Nothing sends real input: every command goes over the loopback automation
channel to the off-screen, non-activating window.
"""
import json
import os
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import autoconn  # noqa: E402
import flicker  # noqa: E402
from PIL import Image, ImageStat  # noqa: E402


class App:
    def __init__(self, port):
        self.s, self.f = autoconn.connect(port, timeout=60)

    def cmd(self, c):
        if c.startswith("sleep "):
            time.sleep(float(c.split()[1]))
            return ""
        self.f.write(c + "\n")
        self.f.flush()
        return self.f.readline().strip()


OPEN = ["key Escape", "key Escape", "select #0", "sleep 1.2"]
# Open and close are separate bursts (as tools/scenarios.py does): tools/flicker.py compares every frame with the burst's first and
# last frame, so a burst that opens and closes an overlay would flag the overlay itself.
SCENARIOS = {
    "ctx_menu": (OPEN, ["menu-open MENU-MAIL 0"], 900),
    "ctx_menu_close": (OPEN + ["menu-open MENU-MAIL 0", "sleep 0.5"], ["key Escape"], 900),
    "dialog": (OPEN, ["dialog-open confirm Verify"], 900),
    "dialog_close": (OPEN + ["dialog-open confirm Verify", "sleep 0.5"], ["dialog-answer cancel"], 900),
    "palette_open": (OPEN, ["palette"], 900),
    "palette_close": (OPEN + ["palette", "sleep 0.5"], ["key Escape"], 900),
    "delete": (OPEN, ["act mail.delete"], 1200),
    "undo": (OPEN + ["act mail.delete", "sleep 0.6"], ["act mail.undo"], 1200),
}


def spread(path):
    """Standard deviation of one frame's luminance: near 0 means a flat (invalid) capture."""
    with Image.open(path) as im:
        return round(ImageStat.Stat(im.convert("L")).stddev[0], 1)


def burst(app, out, ms, action):
    app.cmd(f"burst {ms} 10 {out} 2")
    time.sleep(0.25)
    replies = [app.cmd(c) for c in action]
    while app.cmd("burst-status").startswith("running"):
        time.sleep(0.2)
    while not app.cmd("burst-status").startswith(("done", "none")):
        time.sleep(0.2)
    frames = sorted(f for f in os.listdir(out) if f.startswith("f") and f.endswith(".png")) if os.path.isdir(out) else []
    r = flicker.analyse(out, layout="bento") if frames else {"frames": 0}
    r["replies"] = [x for x in replies if x]
    r["first_spread"] = spread(os.path.join(out, frames[0])) if frames else None
    r["last_spread"] = spread(os.path.join(out, frames[-1])) if frames else None
    r["valid"] = bool(frames) and len(frames) >= 3 and min(r["first_spread"], r["last_spread"]) > 2.0
    return r


def probe(app, action):
    app.cmd("probe-start rendering")
    t0 = time.perf_counter()
    for c in action:
        app.cmd(c)
    took = round((time.perf_counter() - t0) * 1000, 1)
    time.sleep(0.4)  # the transition's frames after the last command
    p = json.loads(app.cmd("probe-stop") or "{}")
    ends = sorted(e for _, e in p.get("frames", []))
    gaps = [b - a for a, b in zip(ends, ends[1:]) if b - a < 50]
    return {"frames": len(ends), "worst_gap_ms": round(max(gaps), 1) if gaps else None, "action_ms": took,
            "verdict": "unproven (capture invalid: no frames)" if len(ends) < 2 else ("ok" if max(gaps or [0]) <= 25 else "over 25 ms")}


def main():
    a = sys.argv[1:]
    opt = lambda k, dflt=None: a[a.index(k) + 1] if k in a else dflt  # noqa: E731
    port = int(opt("--port", "8889"))
    out_dir = os.path.abspath(opt("--dir", os.path.join(ROOT, "artifacts", "auto", str(port), "feel")))
    looks = opt("--looks", "bento-light,bento-dark,nordic-light,nordic-dark").split(",")
    only = opt("--only")
    app = App(port)
    results = {}
    for look in looks:
        style, mode = look.split("-")
        app.cmd(f"ui {style} {mode}")
        time.sleep(0.6)
        for name, (setup, action, ms) in SCENARIOS.items():
            if only and name not in only.split(","):
                continue
            for c in setup:
                app.cmd(c)
            r = burst(app, os.path.join(out_dir, f"{look}_{name}"), ms, action)
            for c in setup:
                app.cmd(c)
            r["probe"] = probe(app, action)
            results[f"{look}/{name}"] = r
            fl = "valid" if r["valid"] else "INVALID"
            print(f"{look}/{name}: frames={r.get('frames')} {fl} blank={r.get('blank')} bad={r.get('bad')} worst={r.get('worst_ms')} "
                  f"probe={r['probe']['frames']}f gap={r['probe']['worst_gap_ms']} {r['probe']['verdict']}", flush=True)
    out = opt("--out")
    if out:
        with open(out, "w", encoding="utf8") as f:
            json.dump(results, f, indent=1)


if __name__ == "__main__":
    main()
