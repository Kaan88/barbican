"""Motion / responsiveness probe for Barbican (design/MOTION.md, "Measuring").

Drives a Barbican started with BARBICAN_AUTOMATION=1 (hidden, no real input) through scripted
interactions and reads the frame probe (`probe-start` / `probe-stop` in src/automation.rs): the
start and end time of every rendered frame and the time every input command was dispatched.

Usage:
  python tools/motion.py --port N [--json OUT.json] [--only NAME,...] [--reps 3] [--layout FILE.json]

Each scenario runs once unrecorded (warm-up: first-use glyph / shadow / layer caches) and then
--reps times recorded; the numbers are medians / maxima over all recorded runs.

Per scenario it reports
  inputs     input commands dispatched
  lat50/max  input -> end of the first frame rendered after it (ms): how soon the screen answers
  frames/in  frames rendered within 250 ms of an input (1-2 = a snap, ~8-12 = a 150 ms animation)
  gap max    longest frame-to-frame gap inside a run of motion (4+ frames less than 50 ms apart;
             a snap is 1-2 frames): > 25 ms means a dropped frame at 60 Hz
  render     RedrawRequested -> next event-loop turn, i.e. render + present, median / max (ms)

Frames are timed from the window's winit event filter (RedrawRequested), since the Skia
renderer's rendering notifier is not called for its Windows surface. A state change usually
shows as a pair of frames a few ms apart.
  settle     last input -> end of the last frame (ms): how long the screen kept moving after it

Scenario coordinates come from the layout file (default: tools/motion-layout.json); see there.
"""
import json, os, socket, statistics, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
INPUT = ("click", "rclick", "dclick", "move", "key", "scroll", "drag", "type")


class App:
    def __init__(self, port):
        self.s = socket.create_connection(("127.0.0.1", port), timeout=30)
        self.f = self.s.makefile("rw", encoding="utf8")

    def cmd(self, c):
        self.f.write(c + "\n")
        self.f.flush()
        return self.f.readline().strip()

    def run(self, steps):
        for c in steps:
            if c.startswith("sleep "):
                time.sleep(float(c.split()[1]))
            else:
                r = self.cmd(c)
                if r not in ("ok",) and not c.startswith(("size", "ui ", "motion")):
                    print("   ", c, "->", r[:120])


def analyse(runs, window=250.0):
    """Metrics over several recordings of the same scenario (medians and maxima across all)."""
    acc = {"inputs": 0, "lats": [], "per": [], "gaps": [], "renders": [], "settles": [], "frames": 0}
    for data in runs:
        one(data, acc, window)
    r = lambda x: None if x is None else round(x, 1)
    med = lambda v: r(statistics.median(v)) if v else None
    return {
        "runs": len(runs),
        "inputs": acc["inputs"] // max(1, len(runs)),
        "lat50": med(acc["lats"]),
        "latmax": r(max(acc["lats"])) if acc["lats"] else None,
        "frames_per_input": med(acc["per"]),
        "gapmax": r(max(acc["gaps"])) if acc["gaps"] else None,
        "render50": med(acc["renders"]),
        "rendermax": r(max(acc["renders"])) if acc["renders"] else None,
        "settle": med(acc["settles"]),
        "frames": acc["frames"] // max(1, len(runs)),
    }


def one(data, acc, window):
    frames = data["frames"]
    ev = [(t, e) for t, e in data["events"] if e.split(" ", 1)[0] in INPUT]
    if not ev:
        return
    lats, per, gaps = acc["lats"], acc["per"], acc["gaps"]
    acc["inputs"] += len(ev)
    acc["frames"] += len(frames)
    for t, _ in ev:
        after = [f for f in frames if f[0] >= t - 0.01]
        if after:
            lats.append(after[0][1] - t)
        win = [f for f in frames if t - 0.01 <= f[0] <= t + window]
        per.append(len(win))
    # frame gaps inside runs of motion: frames less than 50 ms apart chain into a run; a run of
    # 4+ frames is an animation (a snap is 1-2 frames), and a gap over 25 ms inside it a drop
    run = []
    for f in frames + [(1e18, 1e18)]:
        if run and f[0] - run[-1][0] >= 50:
            if len(run) >= 4:
                gaps += [b[0] - a[0] for a, b in zip(run, run[1:])]
            run = []
        run.append(f)
    acc["renders"] += [b - a for a, b in frames]
    last_in = ev[-1][0]
    tail = [f for f in frames if f[0] >= last_in]
    # settle: the last frame of the run of frames that follows the last input (gaps under 60 ms)
    if tail:
        end = tail[0][1]
        for a, b in zip(tail, tail[1:]):
            if b[0] - a[1] > 60:
                break
            end = b[1]
        acc["settles"].append(end - last_in)


def scenarios(L):
    """Each scenario: setup steps (not measured), measured steps, teardown steps."""
    lx, ly0, rh = L["list_x"], L["list_y0"], L["row_h"]
    S = {}
    # pointer sweeping down the message list, one row every 60 ms
    S["hover-list"] = ([f"move {lx} {ly0 - 40}", "sleep 0.4"],
                       [f"every 60 7 move {lx} {ly0}+{rh:.0f}i", "sleep 1.0"],
                       [f"move {L['idle_x']} {L['idle_y']}"])
    S["click-mail"] = ([f"move {L['idle_x']} {L['idle_y']}", "sleep 0.3"],
                       [f"click {lx} {ly0 + rh}", "sleep 1.0", f"click {lx} {ly0}", "sleep 1.0"],
                       [])
    # 20 key-repeat steps (33 ms, Windows' fastest repeat) down and back up the list
    S["arrow-repeat-20"] = ([f"click {lx} {ly0}", "sleep 1.0", f"move {L['idle_x']} {L['idle_y']}", "sleep 0.3"],
                            ["every 33 8 key Down", "sleep 0.45", "every 33 8 key Up", "sleep 0.45", "every 33 4 key Down", "sleep 1.2"],
                            ["every 20 10 key Up", "sleep 0.6"])
    S["context-menu"] = ([f"move {L['idle_x']} {L['idle_y']}", "sleep 0.3"],
                         [f"rclick {lx} {ly0 + rh}", "sleep 0.6", "key Escape", "sleep 0.6"],
                         ["key Escape"])
    S["dialog"] = ([f"move {L['idle_x']} {L['idle_y']}", "sleep 0.3", *L["dialog_setup"]],
                   [*L["dialog_open"], "sleep 0.6", *L["dialog_close"], "sleep 0.6"],
                   [])
    S["app-switch"] = ([f"move {L['idle_x']} {L['idle_y']}", "sleep 0.3"],
                       [f"click {L['rail_x']} {L['rail_cal_y']}", "sleep 0.8", f"click {L['rail_x']} {L['rail_mail_y']}", "sleep 0.8"],
                       [])
    # PIM: calendar views, chips, mini calendar, event dialog (opened and closed with Escape: nothing saved)
    cal = L.get("cal")
    if cal:
        go = [f"click {L['rail_x']} {L['rail_cal_y']}", "sleep 1.5"]
        back = [f"click {L['rail_x']} {L['rail_mail_y']}", "sleep 0.5"]
        S["cal-views"] = ([*go, f"move {L['idle_x']} {L['idle_y']}"],
                          [f"click {cal['week']}", "sleep 0.6", f"click {cal['month']}", "sleep 0.6", f"click {cal['day']}", "sleep 0.6"],
                          [])
        S["cal-nav"] = ([], [f"click {cal['next']}", "sleep 0.3", f"click {cal['next']}", "sleep 0.3", f"click {cal['prev']}", "sleep 0.3", f"click {cal['prev']}", "sleep 0.5"], [])
        S["cal-hover"] = ([f"move {L['idle_x']} {L['idle_y']}", "sleep 0.3"],
                          [f"every 60 6 move {cal['sweep']}", "sleep 0.8"], [f"move {L['idle_x']} {L['idle_y']}"])
        S["cal-dialog"] = ([],
                           [f"click {cal['new']}", "sleep 0.6", f"click {cal['cancel']}", "sleep 0.6"], [])
    return S


def main():
    a = sys.argv[1:]
    port = int(a[a.index("--port") + 1]) if "--port" in a else 8792
    out = a[a.index("--json") + 1] if "--json" in a else None
    only = a[a.index("--only") + 1].split(",") if "--only" in a else None
    reps = int(a[a.index("--reps") + 1]) if "--reps" in a else 3
    lf = a[a.index("--layout") + 1] if "--layout" in a else os.path.join(HERE, "motion-layout.json")
    L = json.load(open(lf))
    app = App(port)
    app.run(L.get("prep", []))
    res = {}
    for name, (setup, steps, teardown) in scenarios(L).items():
        if only and name not in only:
            continue
        runs = []
        # one unrecorded warm-up (first-use caches: glyphs, shadows, layers), then `reps` recorded
        for i in range(reps + 1):
            app.run(setup)
            app.cmd("probe-start")
            time.sleep(0.25)
            app.run(steps)
            data = json.loads(app.cmd("probe-stop"))
            app.run(teardown)
            time.sleep(0.4)
            if i > 0:
                runs.append(data)
        res[name] = analyse(runs)
        res[name]["raw"] = runs
        m = res[name]
        print(f"{name:16} in={m['inputs']:3} lat50={m.get('lat50')} latmax={m.get('latmax')} frames/in={m.get('frames_per_input')} "
              f"gapmax={m.get('gapmax')} render50={m.get('render50')} rendermax={m.get('rendermax')} settle={m.get('settle')}")
    if out:
        json.dump(res, open(out, "w"), indent=1)


if __name__ == "__main__":
    main()
