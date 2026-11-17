"""Feel numbers for the M1 look port (docs/milestones/M1-look-gap.md): switch latency, input to first frame, dropped frames.

Usage: python tools/layout/look_feel.py --port 8871 [--out artifacts/look/feel-<tag>.json]
Needs the AUTOMATION build started hidden with sample data ($env:BARBICAN_SAMPLE='1'; tools/auto-run.ps1 start -Port 8871).

Per style (Bento, Nordic; dark):
  switch_key   8 single `key j` steps 350 ms apart (each opens the next mail: list highlight, reader header, body)
  key_repeat   20 `key j` at 33 ms (rapid selection: row feedback must snap)
  folder       `uia-select` of another folder and back (list content swap)
  style_flip   `ui` to the other style and back (no input event: only the frame cadence is read)
Measured with the `rendering` probe (CompositionTarget.Rendering: one entry per UI-thread frame, [start, end of the frame's work]):
  in_to_frame  input to the end of the first frame after it (ms): median and max
  gap_max      longest frame-to-frame gap within 300 ms after an input (ms); `dropped` counts gaps over 25 ms (more than 1.5 frames at 60 Hz)
  open_to_shown  the body stream's own timing of each switch (`body timing`): open call to the turn that applies header and body
"""
import json, os, statistics, sys, time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import autoconn  # noqa: E402


class Driver:
    def __init__(self, port):
        self.s, self.f = autoconn.connect(port, timeout=60)

    def cmd(self, c):
        self.f.write(c + "\n")
        self.f.flush()
        return self.f.readline().strip()


def stats(p, window=300.0):
    frames = p["frames"]
    events = [t for t, c in p["events"]]
    lat, gaps = [], []
    for t in events:
        after = [f for f in frames if f[1] >= t]
        if after:
            lat.append(min(f[1] for f in after) - t)
        starts = sorted(f[0] for f in frames if t - 20 <= f[0] <= t + window)
        gaps += [b - a for a, b in zip(starts, starts[1:])]
    allstarts = sorted(f[0] for f in frames)
    allgaps = [b - a for a, b in zip(allstarts, allstarts[1:])]
    r = lambda v: round(v, 1)  # noqa: E731
    return {
        "inputs": len(events),
        "frames": len(frames),
        "in_to_frame_med": r(statistics.median(lat)) if lat else None,
        "in_to_frame_max": r(max(lat)) if lat else None,
        "gap_max": r(max(gaps or allgaps or [0])),
        "dropped": sum(1 for g in (gaps or allgaps) if g > 25),
    }


def probe(d, actions):
    d.cmd("probe-start rendering")
    time.sleep(0.25)
    for a in actions:
        if a.startswith("sleep "):
            time.sleep(float(a.split()[1]))
        else:
            d.cmd(a)
    time.sleep(0.4)
    return json.loads(d.cmd("probe-stop"))


def main():
    a = sys.argv[1:]
    opt = lambda k, dflt=None: a[a.index(k) + 1] if k in a else dflt  # noqa: E731
    port = int(opt("--port", "8871"))
    out = opt("--out")
    d = Driver(port)
    ids = [n["id"] for n in json.loads(d.cmd("tree"))]
    rows = [i for i in ids[ids.index("list.rows") + 1:] if i.startswith("e")]
    res = {}
    d.cmd("resize 1400 900")
    for style in ("bento", "nordic"):
        d.cmd(f"ui {style} dark normal")
        time.sleep(0.5)
        d.cmd(f"uia-select {rows[0]}")
        time.sleep(0.6)
        opens = []
        acts = []
        for _ in range(8):
            acts += ["key j", "sleep 0.35"]
        p = probe(d, acts[:2])  # warm one step outside the numbers
        d.cmd(f"uia-select {rows[0]}")
        time.sleep(0.5)
        d.cmd("probe-start rendering")
        time.sleep(0.25)
        for _ in range(7):
            d.cmd("key j")
            time.sleep(0.35)
            t = d.cmd("body timing").split()
            opens.append(float(t[1]) if len(t) > 1 and t[1].replace(".", "", 1).isdigit() else None)
        time.sleep(0.3)
        s = stats(json.loads(d.cmd("probe-stop")))
        o = [x for x in opens if x is not None]
        s["open_to_shown_med"] = round(statistics.median(o), 1) if o else None
        s["open_to_shown_max"] = round(max(o), 1) if o else None
        res[f"{style}/switch_key"] = s
        d.cmd(f"uia-select {rows[0]}")
        time.sleep(0.5)
        res[f"{style}/key_repeat"] = stats(probe(d, ["key j", "sleep 0.033"] * 20))
        res[f"{style}/folder"] = stats(probe(d, ["uia-select m3", "sleep 0.6", "uia-select m1", "sleep 0.6"]))
        other = "nordic" if style == "bento" else "bento"
        res[f"{style}/style_flip"] = stats(probe(d, [f"ui {other} dark normal", "sleep 0.5", f"ui {style} dark normal", "sleep 0.5"]))
        for k in ("switch_key", "key_repeat", "folder", "style_flip"):
            print(f"{style}/{k}", res[f"{style}/{k}"])
    d.cmd("probe-start rendering")
    time.sleep(0.05)
    d.cmd("probe-stop")
    d.cmd("probe-start wgc")
    time.sleep(2)
    res["idle_wgc_frames_2s"] = len(json.loads(d.cmd("probe-stop"))["frames"])
    print("idle wgc frames in 2 s:", res["idle_wgc_frames_2s"])
    if out:
        os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
        json.dump(res, open(out, "w"), indent=1)


if __name__ == "__main__":
    main()
